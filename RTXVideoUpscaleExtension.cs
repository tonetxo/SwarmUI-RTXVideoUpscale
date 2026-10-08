using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
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
    public static T2IParamGroup RTXGroup;

    // Upscale parameters
    public static T2IRegisteredParam<string> RTXUpscaleQuality;
    public static T2IRegisteredParam<string> RTXUpscaleResizeMode;
    public static T2IRegisteredParam<double> RTXUpscaleScale;
    public static T2IRegisteredParam<int> RTXUpscaleTargetWidth;
    public static T2IRegisteredParam<int> RTXUpscaleTargetHeight;
    public static T2IRegisteredParam<double> RTXUpscaleStrength;
    public static T2IRegisteredParam<string> RTXUpscaleEncoding;

    // Frame Interpolation parameters
    public static T2IRegisteredParam<int> RTXFrameInterpolation;
    public static T2IRegisteredParam<bool> RTXFrameGenAdjustFps;
    public static T2IRegisteredParam<string> RTXFrameGenMode;
    public static T2IRegisteredParam<bool> RTXFrameGenShotDetection;
    public static T2IRegisteredParam<string> RTXFrameGenEncoding;

    // Internal history video parameters
    public static T2IRegisteredParam<string> RTXVideoFile;
    public static T2IRegisteredParam<string> RTXInterpolateVideoFile;

    public static readonly string[] QualityOptions =
    [
        "HIGHBITRATE_ULTRA",
        "HIGHBITRATE_HIGH",
        "HIGHBITRATE_MEDIUM",
        "HIGHBITRATE_LOW",
        "ULTRA",
        "HIGH",
        "MEDIUM",
        "LOW",
        "BICUBIC",
        "DENOISE_ULTRA",
        "DENOISE_HIGH",
        "DENOISE_MEDIUM",
        "DENOISE_LOW",
        "DEBLUR_ULTRA",
        "DEBLUR_HIGH",
        "DEBLUR_MEDIUM",
        "DEBLUR_LOW",
        "STREAMING_ULTRA",
        "STREAMING_MEDIUM"
    ];

    public override void OnInit()
    {
        Logs.Init("RTX Video Upscale & Frame Generation Extension initializing...");

        // Register feature mapping for NVIDIA RTX ComfyUI nodes
        ComfyUIBackendExtension.NodeToFeatureMap["RTXVideoSuperResolution"] = "rtx_video_upscale";
        ComfyUIBackendExtension.NodeToFeatureMap["RTXVideoFrameGeneration"] = "rtx_video_frame_gen";
        ComfyUIBackendExtension.NodeToFeatureMap["RTXTrueHDR"] = "rtx_true_hdr";

        // Register installable feature for missing node pack
        InstallableFeatures.RegisterInstallableFeature(new(
            "NVIDIA RTX Nodes",
            "rtx_video_upscale",
            "https://github.com/Comfy-Org/Nvidia_RTX_Nodes_ComfyUI",
            "comfyui",
            "This will install the NVIDIA RTX Nodes ComfyUI node pack (RTX Video Super Resolution / Frame Generation).\nDo you wish to install?"
        ));
        InstallableFeatures.RegisterInstallableFeature(new(
            "NVIDIA RTX Nodes",
            "rtx_video_frame_gen",
            "https://github.com/Comfy-Org/Nvidia_RTX_Nodes_ComfyUI",
            "comfyui",
            "This will install the NVIDIA RTX Nodes ComfyUI node pack (RTX Video Frame Generation).\nDo you wish to install?"
        ));

        // Add the JS file that registers history action buttons
        ScriptFiles.Add("assets/rtx_upscale.js");

        RegisterParameters();

        // Priority -1: Standalone video upscale or frame interpolation from history
        WorkflowGenerator.AddStep(GenerateRTXVideoFileWorkflow, -1);
        // Priority 6: Image generation upscaling (after VAEDecode at 1, before SaveImage at 10)
        WorkflowGenerator.AddStep(GenerateRTXImageWorkflow, 6);
        // Priority 80: Video generation frame interpolation (after decode, before upscale at 90 and True Final Save at 100)
        WorkflowGenerator.AddStep(GenerateRTXFrameGenWorkflow, 80);
        // Priority 90: Video generation upscaling (after interpolation, before True Final Save at 100)
        WorkflowGenerator.AddStep(GenerateRTXVideoGenerationWorkflow, 90);

        Logs.Init("RTX Video Upscale & Frame Generation Extension loaded successfully.");
    }

    private static void RegisterParameters()
    {
        RTXGroup = new(
            Name: "RTX Video Upscale",
            Description: "Upscale video/images and interpolate frames using NVIDIA RTX Video Super Resolution (VSR) and RTX Video Frame Generation.\n" +
                         "Hardware-accelerated by NVIDIA TensorRT and RTX Video Effects SDK.\n" +
                         "Can be enabled for generation or used via history buttons ('RTX Upscale', 'RTX Interpolate').",
            Toggles: true,
            Open: false,
            OrderPriority: 8,
            IsAdvanced: false
        );

        double priority = 0;

        // --- Upscale Parameters ---

        RTXUpscaleQuality = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "RTX Upscale Quality",
            Description: "Quality mode for NVIDIA RTX Video Super Resolution.\n" +
                         "'HIGHBITRATE_ULTRA' (recommended) preserves fine details on clean AI outputs.\n" +
                         "Standard modes: ULTRA, HIGH, MEDIUM, LOW, BICUBIC.\n" +
                         "Denoise and Deblur modes clean artifacts while preserving input resolution.",
            Default: "HIGHBITRATE_ULTRA",
            Group: RTXGroup,
            OrderPriority: priority++,
            FeatureFlag: "rtx_video_upscale",
            DoNotPreview: true,
            GetValues: _ => QualityOptions.ToList()
        ));

        RTXUpscaleResizeMode = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "RTX Upscale Resize Mode",
            Description: "Choose whether to scale by a multiplier (e.g. 2x) or target specific pixel dimensions (width x height).",
            Default: "Scale by Multiplier",
            Group: RTXGroup,
            OrderPriority: priority++,
            FeatureFlag: "rtx_video_upscale",
            DoNotPreview: true,
            GetValues: _ => ["Scale by Multiplier", "Target Dimensions"]
        ));

        RTXUpscaleScale = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "RTX Upscale Scale",
            Description: "Scale multiplier for RTX Super Resolution (e.g. 2.0 doubles width and height; 1.5x scales 720p to 1080p).",
            Default: "2.0",
            Min: 1.0,
            Max: 4.0,
            Step: 0.01,
            Group: RTXGroup,
            OrderPriority: priority++,
            FeatureFlag: "rtx_video_upscale",
            DoNotPreview: true
        ));

        RTXUpscaleTargetWidth = T2IParamTypes.Register<int>(new T2IParamType(
            Name: "RTX Upscale Target Width",
            Description: "Target width in pixels when using 'Target Dimensions' mode (rounded to multiple of 8).",
            Default: "1920",
            Min: 64,
            Max: 8192,
            Step: 8,
            Group: RTXGroup,
            OrderPriority: priority++,
            FeatureFlag: "rtx_video_upscale",
            DoNotPreview: true,
            IsAdvanced: true
        ));

        RTXUpscaleTargetHeight = T2IParamTypes.Register<int>(new T2IParamType(
            Name: "RTX Upscale Target Height",
            Description: "Target height in pixels when using 'Target Dimensions' mode (rounded to multiple of 8).",
            Default: "1080",
            Min: 64,
            Max: 8192,
            Step: 8,
            Group: RTXGroup,
            OrderPriority: priority++,
            FeatureFlag: "rtx_video_upscale",
            DoNotPreview: true,
            IsAdvanced: true
        ));

        RTXUpscaleStrength = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "RTX Upscale Strength",
            Description: "Effect blend strength from 0.0 (unprocessed) to 1.0 (full RTX enhancement).",
            Default: "1.0",
            Min: 0.0,
            Max: 1.0,
            Step: 0.01,
            ViewType: ParamViewType.SLIDER,
            Group: RTXGroup,
            OrderPriority: priority++,
            FeatureFlag: "rtx_video_upscale",
            DoNotPreview: true,
            IsAdvanced: true
        ));

        RTXUpscaleEncoding = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "RTX Upscale Encoding",
            Description: "SDK pixel encoding format. '8-bit RGB' is standard; '10-bit RGB (RGB10A2)' offers higher color precision.",
            Default: "8-bit RGB",
            Group: RTXGroup,
            OrderPriority: priority++,
            FeatureFlag: "rtx_video_upscale",
            DoNotPreview: true,
            IsAdvanced: true,
            GetValues: _ => ["8-bit RGB", "10-bit RGB (RGB10A2)"]
        ));

        // --- Frame Generation (Interpolation) Parameters ---

        RTXFrameInterpolation = T2IParamTypes.Register<int>(new T2IParamType(
            Name: "RTX Frame Generation Multiplier",
            Description: "Frame count multiplier for NVIDIA RTX Video Frame Generation.\n" +
                         "1 = disabled, 2 doubles frame rate (e.g. 16 -> 32 fps), 3 triples it, etc.",
            Default: "1",
            Min: 1,
            Max: 16,
            Step: 1,
            ViewType: ParamViewType.SLIDER,
            Group: RTXGroup,
            OrderPriority: priority++,
            FeatureFlag: "rtx_video_frame_gen",
            DoNotPreview: true
        ));

        RTXFrameGenAdjustFps = T2IParamTypes.Register<bool>(new T2IParamType(
            Name: "RTX Frame Gen Adjust FPS",
            Description: "When enabled, multiplies video FPS with the frame multiplier to preserve original duration and achieve maximum smoothness.\n" +
                         "When disabled, plays at original FPS in slow-motion.",
            Default: "true",
            Group: RTXGroup,
            OrderPriority: priority++,
            FeatureFlag: "rtx_video_frame_gen",
            DoNotPreview: true
        ));

        RTXFrameGenMode = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "RTX Frame Gen Mode",
            Description: "Quality and performance mode for RTX Frame Generation.\n" +
                         "'MEDIUM' (recommended) offers balanced motion clarity and high tensor throughput; 'HIGH' maximizes detail.",
            Default: "MEDIUM",
            Group: RTXGroup,
            OrderPriority: priority++,
            FeatureFlag: "rtx_video_frame_gen",
            DoNotPreview: true,
            GetValues: _ => ["MEDIUM", "HIGH", "LOW"]
        ));

        RTXFrameGenShotDetection = T2IParamTypes.Register<bool>(new T2IParamType(
            Name: "RTX Frame Gen Shot Detection",
            Description: "Automatically detects hard scene cuts to prevent unwanted frame morphing across shot transitions.",
            Default: "true",
            Group: RTXGroup,
            OrderPriority: priority++,
            FeatureFlag: "rtx_video_frame_gen",
            DoNotPreview: true,
            IsAdvanced: true
        ));

        RTXFrameGenEncoding = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "RTX Frame Gen Encoding",
            Description: "Pixel encoding for frame generation. '8-bit RGB' is standard; '10-bit RGB (RGB10A2)' provides higher color fidelity.",
            Default: "8-bit RGB",
            Group: RTXGroup,
            OrderPriority: priority++,
            FeatureFlag: "rtx_video_frame_gen",
            DoNotPreview: true,
            IsAdvanced: true,
            GetValues: _ => ["8-bit RGB", "10-bit RGB (RGB10A2)"]
        ));

        // --- Hidden History Action Parameters ---

        RTXVideoFile = T2IParamTypes.Register<string>(new(
            "RTX Video File",
            "Internal parameter for video upscaling from history.",
            "",
            FeatureFlag: "rtx_video_upscale",
            ChangeWeight: 2,
            VisibleNormally: false
        ));

        RTXInterpolateVideoFile = T2IParamTypes.Register<string>(new(
            "RTX Interpolate Video File",
            "Internal parameter for video frame generation from history.",
            "",
            FeatureFlag: "rtx_video_frame_gen",
            ChangeWeight: 2,
            VisibleNormally: false
        ));
    }

    private static string ResolveVideoPath(User user, string videoFile)
    {
        if (videoFile.StartsWith("~/"))
        {
            videoFile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + videoFile[1..];
        }

        if (user is not null)
        {
            string relativePath = null;
            if (videoFile.StartsWith("Output/"))
            {
                relativePath = videoFile["Output/".Length..];
            }
            else if (videoFile.StartsWith("View/"))
            {
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

        return videoFile;
    }

    private static (string container, string codec) GetVideoFormat(WorkflowGenerator g)
    {
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

        if (container != "mp4" && container != "auto")
        {
            container = "mp4";
        }
        if (codec != "h264" && codec != "auto")
        {
            codec = "auto";
        }
        return (container, codec);
    }

    private static JObject BuildRTXSuperResolutionInputs(WorkflowGenerator g, JArray inputImages)
    {
        string quality = g.UserInput.Get(RTXUpscaleQuality, "HIGHBITRATE_ULTRA");
        string resizeMode = g.UserInput.Get(RTXUpscaleResizeMode, "Scale by Multiplier");
        double scale = g.UserInput.Get(RTXUpscaleScale, 2.0);
        int targetWidth = g.UserInput.Get(RTXUpscaleTargetWidth, 1920);
        int targetHeight = g.UserInput.Get(RTXUpscaleTargetHeight, 1080);
        double strength = g.UserInput.Get(RTXUpscaleStrength, 1.0);
        string encoding = g.UserInput.Get(RTXUpscaleEncoding, "8-bit RGB");

        JObject rtxInputs = new()
        {
            ["images"] = inputImages,
            ["quality"] = quality,
            ["strength"] = strength,
            ["image_encoding"] = encoding
        };

        if (resizeMode.Equals("Target Dimensions", StringComparison.OrdinalIgnoreCase))
        {
            rtxInputs["resize_type"] = "target dimensions";
            rtxInputs["resize_type.width"] = targetWidth;
            rtxInputs["resize_type.height"] = targetHeight;
        }
        else
        {
            rtxInputs["resize_type"] = "scale by multiplier";
            rtxInputs["resize_type.scale"] = scale;
        }

        return rtxInputs;
    }

    private static JObject BuildRTXFrameGenerationInputs(WorkflowGenerator g, JArray inputImages, int multiplier)
    {
        string mode = g.UserInput.Get(RTXFrameGenMode, "MEDIUM");
        bool shotDetection = g.UserInput.Get(RTXFrameGenShotDetection, true);
        string encoding = g.UserInput.Get(RTXFrameGenEncoding, "8-bit RGB");

        return new JObject()
        {
            ["images"] = inputImages,
            ["generation_type"] = "frame rate multiplier",
            ["generation_type.multiplier"] = multiplier,
            ["mode"] = mode,
            ["automatic_shot_change_detection"] = shotDetection,
            ["shot_change"] = false,
            ["image_encoding"] = encoding
        };
    }

    /// <summary>Generates a complete workflow to upscale or interpolate a video file from history.</summary>
    public static void GenerateRTXVideoFileWorkflow(WorkflowGenerator g)
    {
        bool hasUpscaleFile = g.UserInput.TryGet(RTXVideoFile, out string upscaleFile) && !string.IsNullOrEmpty(upscaleFile);
        bool hasInterpFile = g.UserInput.TryGet(RTXInterpolateVideoFile, out string interpFile) && !string.IsNullOrEmpty(interpFile);

        if (!hasUpscaleFile && !hasInterpFile)
        {
            return;
        }

        if (!g.Features.Contains("comfy_loadimage_b64") || WorkflowGenerator.RestrictCustomNodes)
        {
            throw new SwarmUserErrorException("NVIDIA RTX video processing requires SwarmUI's ComfyUI backend nodes (comfy_loadimage_b64) and cannot run with restricted custom nodes.");
        }

        User user = g.UserInput.SourceSession?.User;

        if (hasInterpFile)
        {
            string resolvedPath = ResolveVideoPath(user, interpFile);
            if (!File.Exists(resolvedPath))
            {
                throw new SwarmUserErrorException($"RTX Frame Generation: Video file not found: {resolvedPath}");
            }

            if (!g.Features.Contains("rtx_video_frame_gen"))
            {
                throw new SwarmUserErrorException("RTX Frame Generation requires the NVIDIA RTX Nodes ComfyUI node pack (RTXVideoFrameGeneration).");
            }

            int multiplier = g.UserInput.Get(RTXFrameInterpolation, 2);
            if (multiplier < 2)
            {
                multiplier = 2;
            }
            bool adjustFps = g.UserInput.Get(RTXFrameGenAdjustFps, true);

            Logs.Info($"RTX Frame Generation: Processing video file '{resolvedPath}' (multiplier={multiplier}, adjustFps={adjustFps})");

            byte[] rawVideoData = File.ReadAllBytes(resolvedPath);
            VideoFile sourceVideo = new(rawVideoData, MediaType.GetByExtension(resolvedPath.AfterLast('.').ToLowerFast()));

            WGNodeData videoData = g.LoadVideo(sourceVideo, "${rtx_interp_source_video}", false);
            JArray loadedVideoFrames = videoData.Path;
            JArray videoAudio = videoData.AttachedAudio?.Path;
            JToken videoFps = videoData.FPS;

            string rtxNode = g.CreateNode("RTXVideoFrameGeneration", BuildRTXFrameGenerationInputs(g, loadedVideoFrames, multiplier));
            JArray interpolatedFrames = new() { rtxNode, 0 };

            JToken finalFps = videoFps;
            if (adjustFps && multiplier > 1)
            {
                string multNode = g.CreateNode("CM_FloatBinaryOperation", new JObject()
                {
                    ["op"] = "Mul",
                    ["a"] = videoFps,
                    ["b"] = (double)multiplier
                });
                finalFps = new JArray() { multNode, 0 };
            }

            string createVideoNode = g.CreateNode("CreateVideo", new JObject()
            {
                ["images"] = interpolatedFrames,
                ["audio"] = videoAudio,
                ["fps"] = finalFps
            });

            var (container, codec) = GetVideoFormat(g);
            g.CreateNode("SaveVideo", new JObject()
            {
                ["video"] = new JArray() { createVideoNode, 0 },
                ["filename_prefix"] = "video/RTX_interpolated",
                ["format"] = container,
                ["codec"] = codec
            });

            g.CurrentMedia = new WGNodeData(interpolatedFrames, g, WGNodeData.DT_IMAGE, g.CurrentCompat());
            g.SkipFurtherSteps = true;
            return;
        }

        if (hasUpscaleFile)
        {
            string resolvedPath = ResolveVideoPath(user, upscaleFile);
            if (!File.Exists(resolvedPath))
            {
                throw new SwarmUserErrorException($"RTX Video Upscale: Video file not found: {resolvedPath}");
            }

            if (!g.Features.Contains("rtx_video_upscale"))
            {
                throw new SwarmUserErrorException("RTX Video Upscale requires the NVIDIA RTX Nodes ComfyUI node pack (RTXVideoSuperResolution).");
            }

            Logs.Info($"RTX Video Upscale: Processing video file '{resolvedPath}'");

            byte[] rawVideoData = File.ReadAllBytes(resolvedPath);
            VideoFile sourceVideo = new(rawVideoData, MediaType.GetByExtension(resolvedPath.AfterLast('.').ToLowerFast()));

            WGNodeData videoData = g.LoadVideo(sourceVideo, "${rtx_upscale_source_video}", false);
            JArray loadedVideoFrames = videoData.Path;
            JArray videoAudio = videoData.AttachedAudio?.Path;
            JToken videoFps = videoData.FPS;

            string rtxNode = g.CreateNode("RTXVideoSuperResolution", BuildRTXSuperResolutionInputs(g, loadedVideoFrames));
            JArray upscaledFrames = new() { rtxNode, 0 };

            string createVideoNode = g.CreateNode("CreateVideo", new JObject()
            {
                ["images"] = upscaledFrames,
                ["audio"] = videoAudio,
                ["fps"] = videoFps
            });

            var (container, codec) = GetVideoFormat(g);
            g.CreateNode("SaveVideo", new JObject()
            {
                ["video"] = new JArray() { createVideoNode, 0 },
                ["filename_prefix"] = "video/RTX_upscaled",
                ["format"] = container,
                ["codec"] = codec
            });

            g.CurrentMedia = new WGNodeData(upscaledFrames, g, WGNodeData.DT_IMAGE, g.CurrentCompat());
            g.SkipFurtherSteps = true;
        }
    }

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

    /// <summary>Generates RTX Video Super Resolution nodes for image generation upscaling (Priority 6).</summary>
    public static void GenerateRTXImageWorkflow(WorkflowGenerator g)
    {
        if (g.UserInput.TryGet(RTXVideoFile, out string vFile) && !string.IsNullOrEmpty(vFile))
        {
            return;
        }
        if (g.UserInput.TryGet(RTXInterpolateVideoFile, out string iFile) && !string.IsNullOrEmpty(iFile))
        {
            return;
        }
        if (IsVideoGenerationRequest(g))
        {
            return;
        }
        if (!g.UserInput.TryGet(RTXUpscaleQuality, out _) && !g.UserInput.TryGet(RTXUpscaleScale, out _) && !g.UserInput.TryGet(RTXUpscaleResizeMode, out _))
        {
            return;
        }
        if (!g.Features.Contains("rtx_video_upscale"))
        {
            throw new SwarmUserErrorException("RTX Video Upscale is enabled, but NVIDIA RTX Nodes (ComfyUI_NVIDIA_RTX_Nodes) are not installed or available on this backend.");
        }
        if (g.CurrentMedia is null)
        {
            return;
        }

        Logs.Info("RTX Video Upscale: Upscaling image generation output with RTX Video Super Resolution");

        string rtxNode = g.CreateNode("RTXVideoSuperResolution", BuildRTXSuperResolutionInputs(g, g.CurrentMedia.Path));
        g.CurrentMedia = new WGNodeData(new JArray() { rtxNode, 0 }, g, WGNodeData.DT_IMAGE, g.CurrentCompat());
    }

    /// <summary>Generates RTX Video Frame Generation nodes during live video generation (Priority 80).</summary>
    public static void GenerateRTXFrameGenWorkflow(WorkflowGenerator g)
    {
        if (g.UserInput.TryGet(RTXVideoFile, out string vFile) && !string.IsNullOrEmpty(vFile))
        {
            return;
        }
        if (g.UserInput.TryGet(RTXInterpolateVideoFile, out string iFile) && !string.IsNullOrEmpty(iFile))
        {
            return;
        }
        if (!IsVideoGenerationRequest(g))
        {
            return;
        }

        int multiplier = g.UserInput.Get(RTXFrameInterpolation, 1);
        if (multiplier < 2)
        {
            return;
        }

        if (!g.Features.Contains("rtx_video_frame_gen"))
        {
            throw new SwarmUserErrorException("RTX Frame Generation is enabled, but NVIDIA RTX Nodes (RTXVideoFrameGeneration) are not available on this ComfyUI backend.");
        }
        if (g.CurrentMedia is null)
        {
            return;
        }

        Logs.Info($"RTX Frame Generation: Interpolating video frames {multiplier}x");

        g.CurrentMedia = g.CurrentMedia.AsRawImage(g.CurrentVae);
        JArray decodedFrames = g.CurrentMedia.Path;

        string rtxNode = g.CreateNode("RTXVideoFrameGeneration", BuildRTXFrameGenerationInputs(g, decodedFrames, multiplier));
        JArray interpolatedOutput = new() { rtxNode, 0 };

        g.CurrentMedia = g.CurrentMedia.WithPath(interpolatedOutput);

        if (g.UserInput.Get(RTXFrameGenAdjustFps, true))
        {
            int currentFps = g.CurrentMedia.GetRawFPS() ?? g.Text2VideoFPS();
            int newFps = currentFps * multiplier;
            g.CurrentMedia.FPS = newFps;
            g.T2VFPSOverride = newFps;
            Logs.Info($"RTX Frame Generation: Scaled video FPS from {currentFps} to {newFps}");
        }
    }

    /// <summary>Generates RTX Video Super Resolution nodes for video generation upscaling (Priority 90).</summary>
    public static void GenerateRTXVideoGenerationWorkflow(WorkflowGenerator g)
    {
        if (g.UserInput.TryGet(RTXVideoFile, out string vFile) && !string.IsNullOrEmpty(vFile))
        {
            return;
        }
        if (g.UserInput.TryGet(RTXInterpolateVideoFile, out string iFile) && !string.IsNullOrEmpty(iFile))
        {
            return;
        }
        if (!IsVideoGenerationRequest(g))
        {
            return;
        }

        if (!g.UserInput.TryGet(RTXUpscaleQuality, out _) && !g.UserInput.TryGet(RTXUpscaleScale, out _) && !g.UserInput.TryGet(RTXUpscaleResizeMode, out _))
        {
            return;
        }
        if (!g.Features.Contains("rtx_video_upscale"))
        {
            throw new SwarmUserErrorException("RTX Video Upscale is enabled, but NVIDIA RTX Nodes (ComfyUI_NVIDIA_RTX_Nodes) are not installed or available on this backend.");
        }
        if (g.CurrentMedia is null)
        {
            return;
        }

        Logs.Info("RTX Video Upscale: Upscaling video generation frames with RTX Video Super Resolution");

        g.CurrentMedia = g.CurrentMedia.AsRawImage(g.CurrentVae);
        JArray decodedFrames = g.CurrentMedia.Path;

        string rtxNode = g.CreateNode("RTXVideoSuperResolution", BuildRTXSuperResolutionInputs(g, decodedFrames));
        JArray upscaledOutput = new() { rtxNode, 0 };

        g.CurrentMedia = g.CurrentMedia.WithPath(upscaledOutput);

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
