# Meshia Mesh Simplification (Synthos Edition)

A high-performance, Burst-accelerated mesh decimation library for Unity and VRChat. 

This repository is a customized edition of [Ram.Type-0's Meshia](https://github.com/RamType0/Meshia.MeshSimplification) (under the MIT License), tailored for **both Avatars and Worlds**. It runs standalone without requiring NDMF, integrates directly with tools like **VRCFury**, and powers batch environment processing in tools like **Synthos Scene Optimizer**.

---

## What Was Changed?

1. **Works on Avatars Without NDMF & Better VRCFury Support**
   - **In Stock Meshia:** You could only use it on avatars through NDMF (*Non-Destructive Modular Framework*), requiring multiple extra dependencies and forcing avatars through the NDMF build lifecycle.
   - **In Synthos Edition:** NDMF is completely removed. It works directly on avatars and includes native **VRCFury build hooks** (`MeshiaVrcfuryBuildHook`), allowing VRCFury-managed avatars to decimate meshes cleanly during upload without NDMF framework bloat or order-of-execution conflicts.

2. **Independent UV Barycentric Interpolation (`UseBarycentricCoordinateInterpolationForUV`)**
   - **In Stock Meshia:** Barycentric interpolation was all-or-nothing—forcing surface normals, tangents, and UVs to all interpolate together, which frequently distorted vertex normals and lighting on flat or hard-edged surfaces.
   - **In Synthos Edition:** Normals and tangents can use standard linear interpolation while texture coordinates (UV0–UV7) and vertex colors independently use barycentric interpolation. This preserves lightmaps, trim sheets, and texture mappings with zero shading distortion.

3. **Watertight Edge Preservation (`PreserveBorderEdges = true`)**
   - **In Stock Meshia:** `PreserveBorderEdges` defaulted to `false`, causing holes and seam splits on open meshes and modular building pieces.
   - **In Synthos Edition:** `PreserveBorderEdges` defaults to `true` to keep environmental, clothing, and avatar seam geometry watertight.

4. **Batch Scene & World Processing Support**
   - Optimized for multi-mesh scene decimation jobs via `MeshSimplifier.SimplifyBatch()`, allowing world optimization pipelines to decimate entire scenes in seconds.

---

## What Is No Longer Needed?

- **NDMF (Non-Destructive Modular Framework) is NO LONGER required:** You do not need NDMF installed to optimize your avatars or worlds. If you use **VRCFury**, it hooks directly into the build pipeline seamlessly without NDMF overhead.
- **`com.anatawa12.custom-localization-for-editor-extension` is NO LONGER required:** Stripped unnecessary third-party localization packages.

---

## Installation via VPM (VRChat Creator Companion)

Add the Synthos package repository to VCC / ALCOM:
```
https://grimreaper35487.github.io/Synthos-VRC-Packages/index.json
```
Then add **Meshia Mesh Simplification (Synthos Edition)** to your project, or install **Synthos Scene Optimizer** (which will automatically pull this in as a dependency).

---

## C# Usage

```csharp
using Meshia.MeshSimplification;
using System.Collections.Generic;
using UnityEngine;

// 1. Configure options (PreserveBorderEdges is true by default)
var options = MeshSimplifierOptions.Default;
options.UseBarycentricCoordinateInterpolationForUV = true; // High-accuracy UV preservation

// 2. Set target reduction
var target = new MeshSimplificationTarget
{
    Kind = MeshSimplificationTargetKind.RelativeVertexCount,
    Value = 0.5f // Reduce by 50%
};

// 3. Batch Simplify (Burst accelerated)
var batch = new List<(Mesh Mesh, MeshSimplificationTarget Target, MeshSimplifierOptions Options)>
{
    (myMesh, target, options)
};

MeshSimplifier.SimplifyBatch(batch);
```

---

## Credits & License

* **Original Library:** Developed by [Ram.Type-0](https://github.com/RamType0/Meshia.MeshSimplification) under the [MIT License](LICENSE.md).
* **Modifications & World Optimization:** Maintained by [Synthos](https://github.com/GRIMREAPER35487).
