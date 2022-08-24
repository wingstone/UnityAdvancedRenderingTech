using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class FogLightBind : MonoBehaviour
{
    public RenderTexture RawShadow;

    CommandBuffer grabShadowMap;

    private void OnEnable()
    {

        if (grabShadowMap == null) grabShadowMap = new CommandBuffer();
        if (RawShadow == null) RawShadow = new RenderTexture(1024, 1024, 0, RenderTextureFormat.RFloat);

        RenderTargetIdentifier shadowmap = BuiltinRenderTextureType.CurrentActive;
        grabShadowMap.SetShadowSamplingMode(shadowmap, ShadowSamplingMode.RawDepth);
        grabShadowMap.Blit(BuiltinRenderTextureType.CurrentActive, RawShadow);

        Light light = GetComponent<Light>();
        light.AddCommandBuffer(LightEvent.AfterShadowMap, grabShadowMap);
    }

    private void OnDisable()
    {
        RawShadow.Release();
        grabShadowMap.Release();
    }

}
