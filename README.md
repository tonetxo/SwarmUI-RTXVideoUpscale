# RTX Video Upscale & Frame Generation Extension for SwarmUI

An extension for [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI) that enables NVIDIA RTX Video Super Resolution (VSR) and RTX Video Frame Generation (Interpolation) utilizing TensorRT and NVIDIA RTX Video Effects SDK.

## Features

- **RTX Video Super Resolution (VSR)**:
  - Hardware-accelerated upscaling via NVIDIA RTX tensor cores.
  - Full support for 19 SDK quality levels, including `HIGHBITRATE_ULTRA` (recommended for clean AI video output), `HIGHBITRATE_HIGH`, `ULTRA`, `HIGH`, `DENOISE_*`, and `DEBLUR_*`.
  - Resize modes: Scale multiplier (1.0x - 4.0x) or exact target dimensions (Width x Height).
  - Configurable effect strength and color precision (8-bit RGB or 10-bit RGB).
- **RTX Video Frame Generation**:
  - Intermediate frame interpolation directly on NVIDIA RTX hardware.
  - Multiplier support (2x to 16x frame rate expansion).
  - FPS scaling to preserve original video duration while achieving maximum smoothness.
  - Quality modes: `LOW`, `MEDIUM`, `HIGH`.
  - Automatic shot change detection to prevent blending across hard cuts.
- **Interactive Output History Actions**:
  - `RTX Upscale` button in video context menus to upscale existing videos.
  - `RTX Interpolate` button in video context menus to interpolate existing videos.
