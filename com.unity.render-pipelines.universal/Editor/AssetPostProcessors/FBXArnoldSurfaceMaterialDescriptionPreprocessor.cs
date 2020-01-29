using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Reflection;
using UnityEditor.Experimental.AssetImporters;

class FBXArnoldSurfaceMaterialDescriptionPreprocessor : AssetPostprocessor
{
    static readonly uint k_Version = 2;
    static readonly int k_Order = 4;
    static readonly string k_ShaderPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ArnoldStandardSurface.shadergraph";
    public override uint GetVersion()
    {
        return k_Version;
    }
    public override int GetPostprocessOrder()
    {
        return k_Order;
    }

    static bool IsMayaArnoldStandardSurfaceMaterial(MaterialDescription description)
    {
        float typeId;
        description.TryGetProperty("TypeId", out typeId);
        return typeId == 1138001;
    }
    static bool Is3DsMaxArnoldStandardSurfaceMaterial(MaterialDescription description)
    {
        float classIdA;
        float classIdB;
        description.TryGetProperty("ClassIDa", out classIdA);
        description.TryGetProperty("ClassIDb", out classIdB);
        return classIdA == 2121471519 && classIdB == 1660373836;
    }

    public void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        var lowerCasePath = Path.GetExtension(assetPath).ToLower();
        if (lowerCasePath == ".fbx")
        {
            if (IsMayaArnoldStandardSurfaceMaterial(description))
                CreateFromMayaArnoldStandardSurfaceMaterial(description, material, clips);
            else if (Is3DsMaxArnoldStandardSurfaceMaterial(description))
                CreateFrom3DsMaxArnoldStandardSurfaceMaterial(description, material, clips);
        }
    }

    void CreateFromMayaArnoldStandardSurfaceMaterial(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        float floatProperty;
        Vector4 vectorProperty;
        TexturePropertyDescription textureProperty;

        //context.DependsOnImportedAsset(k_ShaderPath) is internal, use reflection for now..
        var method = typeof(AssetImportContext).GetMethod("DependsOnImportedAsset", BindingFlags.Instance | BindingFlags.NonPublic, null,
            CallingConventions.Any, new Type[] { typeof(string) }, null);

        method.Invoke(context, new object[] { k_ShaderPath });
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(k_ShaderPath);
        if (shader == null)
            return;


        material.shader = shader;
        foreach (var clip in clips)
        {
            clip.ClearCurves();
        }

        float opacity = 1.0f;
        Vector4 opacityColor;
        TexturePropertyDescription opacityMap;
        description.TryGetProperty("opacity", out opacityColor);
        bool hasOpacityMap = description.TryGetProperty("opacity", out opacityMap);
        opacity = Mathf.Min(Mathf.Min(opacityColor.x, opacityColor.y), opacityColor.z);

        float transmission;
        description.TryGetProperty("transmission", out transmission);
        if (opacity == 1.0f && !hasOpacityMap)
        {
            opacity = 1.0f - transmission;
        }

        if (opacity < 1.0f || hasOpacityMap)
        {
            if (hasOpacityMap)
            {
                material.SetTexture("_OPACITY_MAP", opacityMap.texture);
                material.SetFloat("_OPACITY", 1.0f);
            }
            else
            {
                material.SetFloat("_OPACITY", opacity);
            }

            material.SetInt("_SrcBlend", 1);
            material.SetInt("_DstBlend", 10);
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

        description.TryGetProperty("base", out floatProperty);

        if (description.TryGetProperty("baseColor", out textureProperty))
        {
            SetMaterialTextureProperty("_BASE_COLOR_MAP", material, textureProperty);
            material.SetColor("_BASE_COLOR", Color.white * floatProperty);
        }
        else if (description.TryGetProperty("baseColor", out vectorProperty))
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
            remapPropertyColorOrTexture(description, material, "emissionColor", "_EMISSION_COLOR", floatProperty);
        }

        remapPropertyFloatOrTexture(description, material, "metalness", "_METALNESS");

        description.TryGetProperty("specular", out floatProperty);

        remapPropertyColorOrTexture(description, material, "specularColor", "_SPECULAR_COLOR", floatProperty);
        remapPropertyFloatOrTexture(description, material, "specularRoughness", "_SPECULAR_ROUGHNESS");
        remapPropertyFloatOrTexture(description, material, "specularIOR", "_SPECULAR_IOR");

        remapPropertyTexture(description, material, "normalCamera", "_NORMAL_MAP");
    }



    void CreateFrom3DsMaxArnoldStandardSurfaceMaterial(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        float floatProperty;
        Vector4 vectorProperty;
        TexturePropertyDescription textureProperty;

        //context.DependsOnImportedAsset(k_ShaderPath) is internal, use reflection for now..
        var method = typeof(AssetImportContext).GetMethod("DependsOnImportedAsset", BindingFlags.Instance | BindingFlags.NonPublic, null,
            CallingConventions.Any, new Type[] { typeof(string) }, null);

        method.Invoke(context, new object[] { k_ShaderPath });
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(k_ShaderPath);
        if (shader == null)
            return;


        material.shader = shader;
        foreach (var clip in clips)
        {
            clip.ClearCurves();
        }

        float opacity = 1.0f;
        Vector4 opacityColor;
        TexturePropertyDescription opacityMap;
        description.TryGetProperty("opacity", out opacityColor);
        bool hasOpacityMap = description.TryGetProperty("opacity", out opacityMap);
        opacity = Mathf.Min(Mathf.Min(opacityColor.x, opacityColor.y), opacityColor.z);

        if (opacity < 1.0f || hasOpacityMap)
        {
            if (hasOpacityMap)
            {
                material.SetTexture("_OPACITY_MAP", opacityMap.texture);
                material.SetFloat("_OPACITY", 1.0f);
            }
            else
            {
                material.SetFloat("_OPACITY", opacity);
            }

            shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ArnoldStandardSurfaceTransparent.shadergraph");
            material.SetInt("_SrcBlend", 1);
            material.SetInt("_DstBlend", 10);
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_BLENDMODE_ALPHA");
            material.renderQueue = 3000;
        }

        description.TryGetProperty("base", out floatProperty);

        if (description.TryGetProperty("base_color.shader", out textureProperty))
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
            }
            material.SetColor("_BASE_COLOR", vectorProperty * floatProperty);
        }

        if (description.TryGetProperty("emission", out floatProperty) && floatProperty > 0.0f)
        {
            remapPropertyColorOrTexture3DsMax(description, material, "emission_color", "_EMISSION_COLOR", floatProperty);
        }

        remapPropertyFloatOrTexture3DsMax(description, material, "metalness", "_METALNESS");

        description.TryGetProperty("specular", out float specularFactor);

        remapPropertyColorOrTexture3DsMax(description, material, "specular_color", "_SPECULAR_COLOR", specularFactor);
        remapPropertyFloatOrTexture3DsMax(description, material, "specular_roughness", "_SPECULAR_ROUGHNESS");
        remapPropertyFloatOrTexture3DsMax(description, material, "specular_IOR", "_SPECULAR_IOR");

        remapPropertyTexture(description, material, "normal_camera", "_NORMAL_MAP");

    }

    static void SetMaterialTextureProperty(string propertyName, Material material, TexturePropertyDescription textureProperty)
    {
        material.SetTexture(propertyName, textureProperty.texture);
        material.SetTextureOffset(propertyName, textureProperty.offset);
        material.SetTextureScale(propertyName, textureProperty.scale);
    }

    static void remapPropertyFloat(MaterialDescription description, Material material, string inPropName, string outPropName)
    {
        if (description.TryGetProperty(inPropName, out float floatProperty))
        {
            material.SetFloat(outPropName, floatProperty);
        }
    }

    static void remapPropertyTexture(MaterialDescription description, Material material, string inPropName, string outPropName)
    {
        if (description.TryGetProperty(inPropName, out TexturePropertyDescription textureProperty))
        {
            material.SetTexture(outPropName, textureProperty.texture);
        }
    }

    static void remapPropertyColorOrTexture3DsMax(MaterialDescription description, Material material, string inPropName, string outPropName, float multiplier = 1.0f)
    {
        if (description.TryGetProperty(inPropName + ".shader", out TexturePropertyDescription textureProperty))
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

    static void remapPropertyFloatOrTexture3DsMax(MaterialDescription description, Material material, string inPropName, string outPropName)
    {
        if (description.TryGetProperty(inPropName, out TexturePropertyDescription textureProperty))
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

    static void remapPropertyColorOrTexture(MaterialDescription description, Material material, string inPropName, string outPropName, float multiplier = 1.0f)
    {
        if (description.TryGetProperty(inPropName, out TexturePropertyDescription textureProperty))
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
    static void remapPropertyFloatOrTexture(MaterialDescription description, Material material, string inPropName, string outPropName)
    {
        if (description.TryGetProperty(inPropName, out TexturePropertyDescription textureProperty))
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
