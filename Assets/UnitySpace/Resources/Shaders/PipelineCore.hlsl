// The one place the game's hand-written shaders pull in a render pipeline's shader library (U.SPC-T429).
// Every shader includes this instead of a pipeline package path, and calls only the transforms URP and HDRP
// share (com.unity.render-pipelines.core's SpaceTransforms.hlsl: TransformObjectToWorld, TransformWorldToHClip,
// TransformObjectToWorldNormal, ...), so the vertex stage is the same on any pipeline. A pipeline's SubShader is
// what carries its "RenderPipeline" tag; its library is chosen here.
#ifndef UNITYSPACE_PIPELINE_CORE_INCLUDED
#define UNITYSPACE_PIPELINE_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#endif
