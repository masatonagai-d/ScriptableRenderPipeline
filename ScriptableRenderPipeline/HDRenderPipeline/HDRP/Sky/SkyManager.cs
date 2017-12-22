﻿using UnityEngine.Rendering;
using System;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    [Serializable]
    public enum SkyResolution
    {
        SkyResolution128 = 128,
        SkyResolution256 = 256,
        SkyResolution512 = 512,
        SkyResolution1024 = 1024,
        // TODO: Anything above 1024 cause a crash in Unity...
        //SkyResolution2048 = 2048,
        //SkyResolution4096 = 4096
    }

    public enum EnvironementUpdateMode
    {
        OnChanged = 0,
        OnDemand,
        Realtime
    }

    public class BuiltinSkyParameters
    {
        public Matrix4x4                pixelCoordToViewDirMatrix;
        public Matrix4x4                invViewProjMatrix;
        public Vector3                  cameraPosWS;
        public Vector4                  screenSize;
        public CommandBuffer            commandBuffer;
        public Light                    sunLight;
        public RenderTargetIdentifier   colorBuffer;
        public RenderTargetIdentifier   depthBuffer;

        public static RenderTargetIdentifier nullRT = -1;
    }

    public class SkyManager
    {
        Material                m_StandardSkyboxMaterial; // This is the Unity standard skybox material. Used to pass the correct cubemap to Enlighten.
        Material                m_BlitCubemapMaterial;
        Material                m_OpaqueAtmScatteringMaterial;

        bool                    m_UpdateRequired = false;
        bool                    m_NeedUpdateRealtimeEnv = false;
        bool                    m_NeedUpdateBakingSky = false;

        // This is the sky used for rendering in the main view. It will also be used for lighting if no lighting override sky is setup.
        // Ambient Probe: Only for real time GI (otherwise we use the baked one)
        // Reflection Probe : Always used and updated depending on the OnChanged/Realtime flags.
        SkyUpdateContext    m_VisualSky = new SkyUpdateContext();
        // This is optional and is used only to compute ambient probe and sky reflection
        // Ambient Probe: Only for real time GI (otherwise we use the baked one)
        // Reflection Probe : Always used and updated depending on the OnChanged/Realtime flags.
        SkyUpdateContext    m_LightingOverrideSky = new SkyUpdateContext();
        // This is mandatory when using baked GI. This sky is used to setup the global Skybox material used by the GI system to bake sky GI.
        SkyUpdateContext    m_BakingSky = new SkyUpdateContext();

        // The sky rendering contexts holds the render textures used by the sky system.
        // We need to have a separate one for the baking sky because we have to keep it alive regardless of the visual/override sky (because it's set in the lighting panel skybox material).
        SkyRenderingContext m_BakingSkyRenderingContext;
        SkyRenderingContext m_SkyRenderingContext;

        // This interpolation volume stack is used to interpolate the lighting override separately from the visual sky.
        // If a sky setting is present in this volume then it will be used for lighting override.
        VolumeStack         m_LightingOverrideVolumeStack;
        LayerMask           m_LightingOverrideLayerMask = -1;

        public Texture skyReflection { get { return m_SkyRenderingContext.reflectionTexture; } }


        SkySettings GetSkySetting(VolumeStack stack)
        {
            SkySettings result;
            var visualEnv = stack.GetComponent<VisualEnvironment>();
            switch (visualEnv.skyType.value)
            {
                case SkyType.HDRISky:
                    {
                        result = stack.GetComponent<HDRISky>();
                        break;
                    }
                case SkyType.ProceduralSky:
                    {
                        result = stack.GetComponent<ProceduralSky>();
                        break;
                    }
                default:
                    result = null;
                    break;
            }

            return result;
        }

        void UpdateCurrentSkySettings(HDCamera camera)
        {
            m_VisualSky.skySettings = GetSkySetting(VolumeManager.instance.stack);
            m_BakingSky.skySettings = SkySettings.GetBakingSkySettings();
            
            // Update needs to happen before testing if the component is active other internal data structure are not properly updated yet.
            VolumeManager.instance.Update(m_LightingOverrideVolumeStack, camera.camera.transform, m_LightingOverrideLayerMask);
            if(VolumeManager.instance.IsComponentActiveInMask<VisualEnvironment>(m_LightingOverrideLayerMask))
            {
                SkySettings newSkyOverride = GetSkySetting(m_LightingOverrideVolumeStack);
                if(m_LightingOverrideSky.skySettings != null && newSkyOverride == null)
                {
                    // When we switch from override to no override, we need to make sure that the visual sky will actually be properly re-rendered.
                    // Resetting the visual sky hash will ensure that.
                    m_VisualSky.skyParametersHash = -1;
                }
                m_LightingOverrideSky.skySettings = newSkyOverride;
            }
            else
            {
                m_LightingOverrideSky.skySettings = null;
            }
        }

        // Sets the global MIP-mapped cubemap '_SkyTexture' in the shader.
        // The texture being set is the sky (environment) map pre-convolved with GGX.
        public void SetGlobalSkyTexture(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture(HDShaderIDs._SkyTexture, skyReflection);
            float mipCount = Mathf.Clamp(Mathf.Log((float)skyReflection.width, 2.0f) + 1, 0.0f, 6.0f);
            cmd.SetGlobalFloat(HDShaderIDs._SkyTextureMipCount, mipCount);
        }

        public void Build(HDRenderPipelineAsset hdAsset, IBLFilterGGX iblFilterGGX)
        {
            m_BakingSkyRenderingContext = new SkyRenderingContext(iblFilterGGX, (int)hdAsset.renderPipelineSettings.lightLoopSettings.skyReflectionSize, false);
            m_SkyRenderingContext = new SkyRenderingContext(iblFilterGGX, (int)hdAsset.renderPipelineSettings.lightLoopSettings.skyReflectionSize, true);

            m_StandardSkyboxMaterial = CoreUtils.CreateEngineMaterial(hdAsset.renderPipelineResources.skyboxCubemap);
            m_BlitCubemapMaterial = CoreUtils.CreateEngineMaterial(hdAsset.renderPipelineResources.blitCubemap);
            m_OpaqueAtmScatteringMaterial = CoreUtils.CreateEngineMaterial(hdAsset.renderPipelineResources.opaqueAtmosphericScattering);

            m_LightingOverrideVolumeStack = VolumeManager.instance.CreateStack();
            m_LightingOverrideLayerMask = hdAsset.renderPipelineSettings.lightLoopSettings.skyLightingOverrideLayerMask;
        }

        public void Cleanup()
        {
            CoreUtils.Destroy(m_StandardSkyboxMaterial);

            m_BakingSky.Cleanup();
            m_VisualSky.Cleanup();
            m_LightingOverrideSky.Cleanup();

            m_BakingSkyRenderingContext.Cleanup();
            m_SkyRenderingContext.Cleanup();
        }

        public bool IsSkyValid()
        {
            return m_VisualSky.IsValid() || m_LightingOverrideSky.IsValid();
        }


        void BlitCubemap(CommandBuffer cmd, Cubemap source, RenderTexture dest)
        {
            var propertyBlock = new MaterialPropertyBlock();

            for (int i = 0; i < 6; ++i)
            {
                CoreUtils.SetRenderTarget(cmd, dest, ClearFlag.None, 0, (CubemapFace)i);
                propertyBlock.SetTexture("_MainTex", source);
                propertyBlock.SetFloat("_faceIndex", (float)i);
                cmd.DrawProcedural(Matrix4x4.identity, m_BlitCubemapMaterial, 0, MeshTopology.Triangles, 3, 1, propertyBlock);
            }

            // Generate mipmap for our cubemap
            Debug.Assert(dest.autoGenerateMips == false);
            cmd.GenerateMips(dest);
        }

        public void RequestEnvironmentUpdate()
        {
            m_UpdateRequired = true;
        }


        public void UpdateEnvironment(HDCamera camera, Light sunLight, CommandBuffer cmd)
        {
            // WORKAROUND for building the player.
            // When building the player, for some reason we end up in a state where frameCount is not updated but all currently setup shader texture are reset to null
            // resulting in a rendering error (compute shader property not bound) that makes the player building fails...
            // So we just check if the texture is bound here so that we can setup a pink one to avoid the error without breaking half the world.
            if (Shader.GetGlobalTexture(HDShaderIDs._SkyTexture) == null)
                cmd.SetGlobalTexture(HDShaderIDs._SkyTexture, CoreUtils.magentaCubeTexture);

            // This is done here because we need to wait for one frame that the command buffer is executed before using the resulting textures.
            // Testing the current skybox material is because we have to make sure that additive scene loading or even some user script haven't altered it.
            if (m_NeedUpdateBakingSky || (RenderSettings.skybox != m_StandardSkyboxMaterial))
            {
                // Here we update the global SkyMaterial so that it uses our baking sky cubemap. This way, next time the GI is baked, the right sky will be present.
                float intensity = m_BakingSky.IsValid() ? 1.0f : 0.0f; // Eliminate all diffuse if we don't have a skybox (meaning for now the background is black in HDRP)
                m_StandardSkyboxMaterial.SetTexture("_Tex", m_BakingSkyRenderingContext.cubemapRT);
                RenderSettings.skybox = m_StandardSkyboxMaterial; // Setup this material as the default to be use in RenderSettings
                RenderSettings.ambientIntensity = intensity;
                RenderSettings.ambientMode = AmbientMode.Skybox; // Force skybox for our HDRI
                RenderSettings.reflectionIntensity = intensity;
                RenderSettings.customReflection = null;

                // Strictly speaking, this should not be necessary, but it helps avoiding inconsistent behavior in the editor
                // where the GI system sometimes update the ambient probe and sometime does not...
                DynamicGI.UpdateEnvironment();

                m_NeedUpdateBakingSky = false;
            }

            if (m_NeedUpdateRealtimeEnv)
            {
                // TODO: Here we need to do that in case we are using real time GI. Unfortunately we don't have a way to check that atm.
                // Moreover we still need Async readback from texture in command buffers first.
                //DynamicGI.SetEnvironmentData();
                m_NeedUpdateRealtimeEnv = false;
            }

            UpdateCurrentSkySettings(camera);

            m_NeedUpdateBakingSky = m_BakingSkyRenderingContext.UpdateEnvironment(m_BakingSky, camera, sunLight, m_UpdateRequired, cmd);
            SkyUpdateContext currentSky = m_LightingOverrideSky.IsValid() ? m_LightingOverrideSky : m_VisualSky;
            m_NeedUpdateRealtimeEnv = m_SkyRenderingContext.UpdateEnvironment(currentSky, camera, sunLight, m_UpdateRequired, cmd);

            m_UpdateRequired = false;

            SetGlobalSkyTexture(cmd);
            if (IsSkyValid())
            {
                cmd.SetGlobalInt(HDShaderIDs._EnvLightSkyEnabled, 1);
            }
            else
            {
                cmd.SetGlobalInt(HDShaderIDs._EnvLightSkyEnabled, 0);
            }
        }

        public void RenderSky(HDCamera camera, Light sunLight, RenderTargetIdentifier colorBuffer, RenderTargetIdentifier depthBuffer, CommandBuffer cmd)
        {
            m_SkyRenderingContext.RenderSky(m_VisualSky, camera, sunLight, colorBuffer, depthBuffer, cmd);
        }

        public void RenderOpaqueAtmosphericScattering(CommandBuffer cmd)
        {
            using (new ProfilingSample(cmd, "Opaque Atmospheric Scattering"))
            {
                CoreUtils.DrawFullScreen(cmd, m_OpaqueAtmScatteringMaterial);
            }
        }

        public Texture2D ExportSkyToTexture()
        {
            if (!m_VisualSky.IsValid())
            {
                Debug.LogError("Cannot export sky to a texture, no Sky is setup.");
                return null;
            }

            RenderTexture skyCubemap = m_SkyRenderingContext.cubemapRT;

            int resolution = skyCubemap.width;

            var tempRT = new RenderTexture(resolution * 6, resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex2D,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear
            };
            tempRT.Create();

            var temp = new Texture2D(resolution * 6, resolution, TextureFormat.RGBAFloat, false);
            var result = new Texture2D(resolution * 6, resolution, TextureFormat.RGBAFloat, false);

            // Note: We need to invert in Y the cubemap faces because the current sky cubemap is inverted (because it's a RT)
            // So to invert it again so that it's a proper cubemap image we need to do it in several steps because ReadPixels does not have scale parameters:
            // - Convert the cubemap into a 2D texture
            // - Blit and invert it to a temporary target.
            // - Read this target again into the result texture.
            int offset = 0;
            for (int i = 0; i < 6; ++i)
            {
                UnityEngine.Graphics.SetRenderTarget(skyCubemap, 0, (CubemapFace)i);
                temp.ReadPixels(new Rect(0, 0, resolution, resolution), offset, 0);
                temp.Apply();
                offset += resolution;
            }

            // Flip texture.
            UnityEngine.Graphics.Blit(temp, tempRT, new Vector2(1.0f, -1.0f), new Vector2(0.0f, 0.0f));

            result.ReadPixels(new Rect(0, 0, resolution * 6, resolution), 0, 0);
            result.Apply();

            UnityEngine.Graphics.SetRenderTarget(null);
            Object.DestroyImmediate(temp);
            Object.DestroyImmediate(tempRT);

            return result;
        }
    }
}
