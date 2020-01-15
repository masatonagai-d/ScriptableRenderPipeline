using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnityEditor.Rendering.HighDefinition
{
    public class PhysicalMaterialPreprocessor : AssetPostprocessor
    {
        static readonly uint k_Version = 3;
        static readonly int k_Order = 4;

        public override uint GetVersion()
        {
            return k_Version;
        }

        public override int GetPostprocessOrder()
        {
            return k_Order;
        }

        static bool Is3DsMaxPhysicalMaterial(MaterialDescription description)
        {
            float classIdA;
            float classIdB;
            description.TryGetProperty("ClassIDa", out classIdA);
            description.TryGetProperty("ClassIDb", out classIdB);
            return classIdA == 1030429932 && classIdB == -559038463;
        }

        public void OnPreprocessMaterialDescription(MaterialDescription description, Material material,
            AnimationClip[] clips)
        {
            if (Is3DsMaxPhysicalMaterial(description))
            {
                CreateFrom3DsPhysicalMaterial(description, material, clips);
            }
        }

        void CreateFrom3DsPhysicalMaterial(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            float floatProperty;
            Vector4 vectorProperty;
            TexturePropertyDescription textureProperty;

            Shader shader;
            shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/PhysicalMaterial/PhysicalMaterial.ShaderGraph");
            if (shader == null)
                return;

            material.shader = shader;
            foreach (var clip in clips)
            {
                clip.ClearCurves();
            }

            description.TryGetProperty("transparency", out float transparency);
            bool hasTransparencyMap =
                description.TryGetProperty("transparency_map", out TexturePropertyDescription transparencyMap);

            if (transparency > 0.0f || hasTransparencyMap)
            {
                if (hasTransparencyMap)
                {
                    material.SetTexture("_TRANSPARENCY_MAP", transparencyMap.texture);
                    material.SetFloat("_TRANSPARENCY", 1);
                }
                else
                {
                    material.SetFloat("_TRANSPARENCY", transparency);
                }

                material.SetInt("_SrcBlend", 1);
                material.SetInt("_DstBlend", 10);
                //material.SetInt("_ZWrite", 1);
                material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.EnableKeyword("_BLENDMODE_PRESERVE_SPECULAR_LIGHTING");
                material.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                material.EnableKeyword("_BLENDMODE_ALPHA");
                material.renderQueue = 3000;
            }
            else
            {
                material.EnableKeyword("_DOUBLESIDED_ON");
                material.SetInt("_CullMode", 0);
                material.SetInt("_CullModeForward", 0);
                material.doubleSidedGI = true;
            }

            description.TryGetProperty("base_weight", out floatProperty);

            if (description.TryGetProperty("base_color_map", out textureProperty))
            {
                SetMaterialTextureProperty("_BASE_COLOR_MAP", material, textureProperty);
                material.SetColor("_BASE_COLOR", Color.white * floatProperty);
            }
            else if (description.TryGetProperty("base_color", out vectorProperty))
            {
                if (QualitySettings.activeColorSpace == ColorSpace.Gamma)
                {
                    vectorProperty.x = Mathf.LinearToGammaSpace(vectorProperty.x);
                    vectorProperty.y = Mathf.LinearToGammaSpace(vectorProperty.y);
                    vectorProperty.z = Mathf.LinearToGammaSpace(vectorProperty.z);
                    vectorProperty *= floatProperty;
                }

                material.SetColor("_BASE_COLOR", vectorProperty * floatProperty);
            }

            if (description.TryGetProperty("emission", out floatProperty) && floatProperty > 0.0f)
            {
                remapPropertyColorOrTexture3DsMax(description, material, "emit_color", "_EMISSION_COLOR",
                    floatProperty);
            }
            // emit_luminance
            // emit_kelvin

            remapPropertyFloatOrTexture3DsMax(description, material, "metalness", "_METALNESS");
            remapPropertyColorOrTexture3DsMax(description, material, "refl_color", "_SPECULAR_COLOR");
            remapPropertyFloatOrTexture3DsMax(description, material, "roughness", "_SPECULAR_ROUGHNESS");
            remapPropertyFloatOrTexture3DsMax(description, material, "reflectivity", "_SPECULAR_IOR");
            remapPropertyFloatOrTexture3DsMax(description, material, "anisotropy", "_SPECULAR_ANISOTROPY");

            remapPropertyTexture(description, material, "bump_map", "_NORMAL_MAP");

            remapPropertyFloat(description, material, "coating", "_COAT_WEIGHT");
            remapPropertyColorOrTexture3DsMax(description, material, "coat_color", "_COAT_COLOR");
            remapPropertyFloatOrTexture3DsMax(description, material, "coat_roughness", "_COAT_ROUGHNESS");
            remapPropertyFloatOrTexture3DsMax(description, material, "coat_ior", "_COAT_IOR");
            remapPropertyTexture(description, material, "coat_bump_map", "_COAT_NORMAL");
            // coat_roughness_inv
            // coat_affect_color
            // coat_affect_roughness
        }

        static void SetMaterialTextureProperty(string propertyName, Material material,
            TexturePropertyDescription textureProperty)
        {
            material.SetTexture(propertyName, textureProperty.texture);
            material.SetTextureOffset(propertyName, textureProperty.offset);
            material.SetTextureScale(propertyName, textureProperty.scale);
        }

        static void remapPropertyFloat(MaterialDescription description, Material material, string inPropName,
            string outPropName)
        {
            if (description.TryGetProperty(inPropName, out float floatProperty))
            {
                material.SetFloat(outPropName, floatProperty);
            }
        }

        static void remapPropertyTexture(MaterialDescription description, Material material, string inPropName,
            string outPropName)
        {
            if (description.TryGetProperty(inPropName, out TexturePropertyDescription textureProperty))
            {
                material.SetTexture(outPropName, textureProperty.texture);
            }
        }

        static void remapPropertyColorOrTexture3DsMax(MaterialDescription description, Material material,
            string inPropName, string outPropName, float multiplier = 1.0f)
        {
            if (description.TryGetProperty(inPropName + "_map", out TexturePropertyDescription textureProperty))
            {
                material.SetTexture(outPropName + "_MAP", textureProperty.texture);
                material.SetColor(outPropName, Color.white * multiplier);
            }
            else
            {
                description.TryGetProperty(inPropName, out Vector4 vectorProperty);
                material.SetColor(outPropName, vectorProperty * multiplier);
            }
        }

        static void remapPropertyFloatOrTexture3DsMax(MaterialDescription description, Material material,
            string inPropName, string outPropName)
        {
            if (description.TryGetProperty(inPropName + "_map", out TexturePropertyDescription textureProperty))
            {
                material.SetTexture(outPropName + "_MAP", textureProperty.texture);
                material.SetFloat(outPropName, 1.0f);
            }
            else
            {
                description.TryGetProperty(inPropName, out float floatProperty);
                material.SetFloat(outPropName, floatProperty);
            }
        }
    }
}
