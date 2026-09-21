using System;
using System.IO;
using System.Linq;
using FreneticUtilities.FreneticExtensions;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using SwarmUI.Builtin_ComfyUIBackend;
using Newtonsoft.Json.Linq;

namespace RTXVideoUpscale;

public class RTXVideoUpscaleExtension : Extension
{
    public static T2IParamGroup RTXUpscaleGroup;

    public static T2IRegisteredParam<string> RTXVideoFile;
    public static T2IRegisteredParam<string> RTXUpscaleQuality;
    public static T2IRegisteredParam<double> RTXUpscaleScale;

    public override void OnInit()
    {
        Logs.Init("RTX Video Upscale Extension initializing...");

        // Register feature mapping so SwarmUI detects the RTX node pack and exposes the feature flag.
        ComfyUIBackendExtension.NodeToFeatureMap["RTXVideoSuperResolution"] = "rtx_video_upscale";

        // Register installable feature so the node pack can be installed from the UI if missing.
        InstallableFeatures.RegisterInstallableFeature(new(
            "NVIDIA RTX Nodes",
            "rtx_video_upscale",
            "https://github.com/Comfy-Org/Nvidia_RTX_Nodes_ComfyUI",
            "comfyui",
            "This will install the NVIDIA RTX Nodes ComfyUI node pack (RTX Video Super Resolution).\nDo you wish to install?"
        ));

        // Add the JS file that registers the history button.
        ScriptFiles.Add("assets/rtx_upscale.js");

        RegisterParameters();

        // Priority -1: Video file upscaling from history (creates entire workflow if video file is set)
        WorkflowGenerator.AddStep(GenerateRTXVideoFileWorkflow, -1);
        // Priority 6: Image generation upscaling (after VAEDecode at 1, before SaveImage at 10)
        WorkflowGenerator.AddStep(GenerateRTXImageWorkflow, 6);
        // Priority 90: Video generation upscaling (after video decode/trim/interpolation at 50, before True Final Save at 100)
        WorkflowGenerator.AddStep(GenerateRTXVideoGenerationWorkflow, 90);

        Logs.Init("RTX Video Upscale Extension loaded successfully.");
    }

    private static void RegisterParameters()
    {
        RTXUpscaleGroup = new(
            Name: "RTX Video Upscale",
            Description: "Upscale video or image output using NVIDIA RTX Video Super Resolution.\n" +
                         "Enable this group during generation, or select an existing video in output history and click 'RTX Upscale'.\n" +
                         "Requires the NVIDIA RTX Nodes ComfyUI node pack.",
            Toggles: true,
            Open: false,
            OrderPriority: 8,
            IsAdvanced: false
        );

        double priority = 0;

        RTXUpscaleQuality = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "RTX Upscale Quality",
            Description: "Quality level for RTX Video Super Resolution.\n" +
                         "LOW, MEDIUM, HIGH, or ULTRA (default).\n" +
                         "Higher quality is slower but produces better results.",
            Default: "ULTRA",
            Group: RTXUpscaleGroup,
            OrderPriority: priority++,
            FeatureFlag: "rtx_video_upscale",
            DoNotPreview: true,
            GetValues: _ => ["LOW", "MEDIUM", "HIGH", "ULTRA"]
        ));

        RTXUpscaleScale = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "RTX Upscale Scale",
            Description: "Scale factor for RTX Video Super Resolution (e.g. 2.0 doubles the size).",
            Default: "2.0",
            Min: 1.0,
            Max: 4.0,
            Step: 0.01,
            Group: RTXUpscaleGroup,
            OrderPriority: priority++,
            FeatureFlag: "rtx_video_upscale",
            DoNotPreview: true
        ));

        // Hidden parameter - used internally by the RTX Upscale button in output history.
        RTXVideoFile = T2IParamTypes.Register<string>(new(
            "RTX Video File",
            "Internal parameter for video file upscaling.",
            "",
            FeatureFlag: "rtx_video_upscale",
            ChangeWeight: 2,
            VisibleNormally: false
        ));
    }

    /// <summary>Generates a complete workflow to upscale a video file with RTX Video Super Resolution.</summary>
    /// <remarks>
    /// Runs at priority -1 (before all generation). When <see cref="RTXVideoFile"/> is set, it creates
    /// the entire workflow (load video -> RTX upscale -> recombine audio -> save) and skips all other steps.
    /// </remarks>
    public static void GenerateRTXVideoFileWorkflow(WorkflowGenerator g)
    {
        // Only activate if a video file path is set.
        if (!g.UserInput.TryGet(RTXVideoFile, out string videoFile) || string.IsNullOrEmpty(videoFile))
        {
            return;
        }

        Logs.Info($"RTX Video Upscale: Processing video '{videoFile}'");

        // Verify the RTX node pack is available.
        if (!g.Features.Contains("rtx_video_upscale"))
        {
            throw new SwarmUserErrorException("RTX Video Upscale requires the NVIDIA RTX Nodes ComfyUI node pack. Please install it (ComfyUI_NVIDIA_RTX_Nodes).");
        }

        // The video is embedded as base64 (see below), so this backend feature is required.
        if (!g.Features.Contains("comfy_loadimage_b64") || WorkflowGenerator.RestrictCustomNodes)
        {
            throw new SwarmUserErrorException("RTX Video Upscale requires SwarmUI's ComfyUI backend nodes (comfy_loadimage_b64) and cannot run with restricted custom nodes.");
        }

        // Expand path (support ~ for home directory).
        if (videoFile.StartsWith("~/"))
        {
            videoFile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + videoFile[1..];
        }

        // Resolve Output/ or View/ paths to actual file system paths using user's output directory.
        User user = g.UserInput.SourceSession?.User;
        if (user is not null)
        {
            string relativePath = null;
            if (videoFile.StartsWith("Output/"))
            {
                relativePath = videoFile["Output/".Length..];
            }
            else if (videoFile.StartsWith("View/"))
            {
                // View/{user_id}/path format - strip View/{user_id}/ prefix.
                string afterView = videoFile["View/".Length..];
                int slashIndex = afterView.IndexOf('/');
                if (slashIndex > 0)
                {
                    relativePath = afterView[(slashIndex + 1)..];
                }
            }

            if (relativePath is not null)
            {
                string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, user.OutputDirectory);
                videoFile = UserImageHistoryHelper.GetRealPathFor(user, $"{root}/{relativePath}", root: root);
            }
        }

        // Validate the file exists.
        if (!File.Exists(videoFile))
        {
            throw new SwarmUserErrorException($"RTX Video Upscale: Video file not found: {videoFile}");
        }

        // Read the file into memory so it can be embedded directly in the workflow (via base64),
        // rather than handing ComfyUI a host filesystem path.
        VideoFile sourceVideo;
        try
        {
            byte[] rawVideoData = File.ReadAllBytes(videoFile);
            sourceVideo = new VideoFile(rawVideoData, MediaType.GetByExtension(videoFile.AfterLast('.').ToLowerFast()));
        }
        catch (Exception ex)
        {
            throw new SwarmUserErrorException($"RTX Video Upscale: Could not read video: {ex.Message}");
        }

        // Get upscale parameters.
        string quality = g.UserInput.Get(RTXUpscaleQuality, "ULTRA");
        double scale = g.UserInput.Get(RTXUpscaleScale, 2.0);

        Logs.Info($"RTX Video Upscale: quality={quality}, scale={scale}");

        // 1. Load the video - embedded as base64 via SwarmUI's LoadVideo helper.
        WGNodeData videoData = g.LoadVideo(sourceVideo, "${rtx_source_video}", false);
        JArray loadedVideoFrames = videoData.Path;
        JArray videoAudio = videoData.AttachedAudio?.Path;
        JArray videoFps = (JArray)videoData.FPS;

        // 2. RTX Video Super Resolution - upscale the video frames.
        JObject rtxInputs = new JObject()
        {
            ["images"] = loadedVideoFrames,
            ["resize_type"] = "scale by multiplier",
            ["resize_type.scale"] = scale,
            ["quality"] = quality
        };
        string rtxNode = g.CreateNode("RTXVideoSuperResolution", rtxInputs);
        JArray upscaledFrames = new JArray() { rtxNode, 0 };

        // 3. CreateVideo - combine upscaled frames with original audio.
        string createVideoNode = g.CreateNode("CreateVideo", new JObject()
        {
            ["images"] = upscaledFrames,
            ["audio"] = videoAudio,
            ["fps"] = videoFps
        });

        // 4. SaveVideo - save the upscaled result.
        string swarmVideoFormat = g.UserInput.Get(T2IParamTypes.VideoFormat, "h264-mp4");
        string container = "mp4";
        string codec = "auto";
        if (swarmVideoFormat.Contains("-"))
        {
            string[] parts = swarmVideoFormat.Split('-');
            codec = parts[0];
            container = parts[1];
        }
        else
        {
            container = swarmVideoFormat;
        }
        // ComfyUI SaveVideo only supports "mp4" or "auto" containers.
        if (container != "mp4" && container != "auto")
        {
            container = "mp4";
        }
        // ComfyUI SaveVideo only supports "h264" or "auto" codecs.
        if (codec != "h264" && codec != "auto")
        {
            codec = "auto";
        }
        g.CreateNode("SaveVideo", new JObject()
        {
            ["video"] = new JArray() { createVideoNode, 0 },
            ["filename_prefix"] = "video/RTX_upscaled",
            ["format"] = container,
            ["codec"] = codec
        });

        // Mark workflow as complete - skip all other generation steps.
        g.CurrentMedia = new WGNodeData(upscaledFrames, g, WGNodeData.DT_IMAGE, g.CurrentCompat());
        g.SkipFurtherSteps = true;

        Logs.Info($"RTX Video Upscale: Complete workflow created - loading '{videoFile}', upscaling {scale}x ({quality}), saving as {codec}-{container}");
    }

    /// <summary>Returns true if the current generation request is producing video output.</summary>
    private static bool IsVideoGenerationRequest(WorkflowGenerator g)
    {
        if (g.IsVideoModel())
        {
            return true;
        }
        if (g.UserInput.TryGet(T2IParamTypes.VideoModel, out _))
        {
            return true;
        }
        if (g.UserInput.Get(T2IParamTypes.Prompt, "").Contains("<extend:"))
        {
            return true;
        }
        if (g.CurrentMedia is not null && (g.CurrentMedia.DataType == WGNodeData.DT_VIDEO || g.CurrentMedia.DataType == WGNodeData.DT_LATENT_VIDEO || g.CurrentMedia.DataType == WGNodeData.DT_LATENT_AUDIOVIDEO))
        {
            return true;
        }
        return false;
    }

    /// <summary>Generates RTX Video Super Resolution nodes for image generation upscaling.</summary>
    /// <remarks>
    /// Runs at priority 6: after VAEDecode (1) and before SaveImage (10).
    /// </remarks>
    public static void GenerateRTXImageWorkflow(WorkflowGenerator g)
    {
        // Skip if video file mode is active.
        if (g.UserInput.TryGet(RTXVideoFile, out string videoFile) && !string.IsNullOrEmpty(videoFile))
        {
            return;
        }
        // Only run for image generations.
        if (IsVideoGenerationRequest(g))
        {
            return;
        }
        // Only activate if the group toggle is enabled.
        if (!g.UserInput.TryGet(RTXUpscaleQuality, out _) && !g.UserInput.TryGet(RTXUpscaleScale, out _))
        {
            return;
        }
        // Verify RTX node pack is available.
        if (!g.Features.Contains("rtx_video_upscale"))
        {
            throw new SwarmUserErrorException("RTX Video Upscale is enabled, but NVIDIA RTX Nodes (ComfyUI_NVIDIA_RTX_Nodes) are not installed in ComfyUI.");
        }
        if (g.CurrentMedia is null)
        {
            return;
        }

        string quality = g.UserInput.Get(RTXUpscaleQuality, "ULTRA");
        double scale = g.UserInput.Get(RTXUpscaleScale, 2.0);

        Logs.Info($"RTX Video Upscale: Upscaling image generation output {scale}x ({quality})");

        JObject rtxInputs = new JObject()
        {
            ["images"] = g.CurrentMedia.Path,
            ["resize_type"] = "scale by multiplier",
            ["resize_type.scale"] = scale,
            ["quality"] = quality
        };
        string rtxNode = g.CreateNode("RTXVideoSuperResolution", rtxInputs);
        g.CurrentMedia = new WGNodeData(new JArray() { rtxNode, 0 }, g, WGNodeData.DT_IMAGE, g.CurrentCompat());
    }

    /// <summary>Generates RTX Video Super Resolution nodes for video generation upscaling.</summary>
    /// <remarks>
    /// Runs at priority 90: after video generation, decode, and cleanups (trim/interpolation at 50),
    /// and before True Final Save (priority 100).
    /// </remarks>
    public static void GenerateRTXVideoGenerationWorkflow(WorkflowGenerator g)
    {
        // Skip if video file mode is active.
        if (g.UserInput.TryGet(RTXVideoFile, out string videoFile) && !string.IsNullOrEmpty(videoFile))
        {
            return;
        }
        // Only run for video generations.
        if (!IsVideoGenerationRequest(g))
        {
            return;
        }
        // Only activate if the group toggle is enabled.
        if (!g.UserInput.TryGet(RTXUpscaleQuality, out _) && !g.UserInput.TryGet(RTXUpscaleScale, out _))
        {
            return;
        }
        // Verify RTX node pack is available.
        if (!g.Features.Contains("rtx_video_upscale"))
        {
            throw new SwarmUserErrorException("RTX Video Upscale is enabled, but NVIDIA RTX Nodes (ComfyUI_NVIDIA_RTX_Nodes) are not installed in ComfyUI.");
        }
        if (g.CurrentMedia is null)
        {
            return;
        }

        string quality = g.UserInput.Get(RTXUpscaleQuality, "ULTRA");
        double scale = g.UserInput.Get(RTXUpscaleScale, 2.0);

        Logs.Info($"RTX Video Upscale: Upscaling video generation frames {scale}x ({quality})");

        // Ensure latent video frames are decoded to raw IMAGE before passing to RTX Video Super Resolution
        g.CurrentMedia = g.CurrentMedia.AsRawImage(g.CurrentVae);
        JArray decodedFrames = g.CurrentMedia.Path;

        JObject rtxInputs = new JObject()
        {
            ["images"] = decodedFrames,
            ["resize_type"] = "scale by multiplier",
            ["resize_type.scale"] = scale,
            ["quality"] = quality
        };
        string rtxNode = g.CreateNode("RTXVideoSuperResolution", rtxInputs);
        JArray upscaledOutput = new JArray() { rtxNode, 0 };

        // Update CurrentMedia so True Final Save (step 100) saves the upscaled video frames
        g.CurrentMedia = g.CurrentMedia.WithPath(upscaledOutput);

        // Also update any pre-existing save or boomerang nodes if present
        string originalNodeId = decodedFrames[0].ToString();
        int originalOutputIndex = decodedFrames[1].Value<int>();

        g.RunOnNodesOfClass("SwarmSaveAnimationWS", (nodeId, nodeData) =>
        {
            if (nodeData["inputs"]?["images"] is JArray imagesInput &&
                imagesInput[0].ToString() == originalNodeId &&
                imagesInput[1].Value<int>() == originalOutputIndex)
            {
                nodeData["inputs"]["images"] = upscaledOutput;
            }
        });

        g.RunOnNodesOfClass("SwarmVideoBoomerang", (nodeId, nodeData) =>
        {
            if (nodeData["inputs"]?["images"] is JArray imagesInput &&
                imagesInput[0].ToString() == originalNodeId &&
                imagesInput[1].Value<int>() == originalOutputIndex)
            {
                nodeData["inputs"]["images"] = upscaledOutput;
            }
        });
    }
}
