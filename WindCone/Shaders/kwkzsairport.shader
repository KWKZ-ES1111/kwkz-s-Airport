Shader "cunstom/kwkzsairport"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        [Header(Rendering)]
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        // 核心 1：关闭背面剔除，实现双面渲染
        Cull Off

        CGPROGRAM
        // 核心 2：addshadow 强制 Unity 为双面生成正确的阴影深度图
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
            // 核心 3：获取面朝向信息 (vface)，1 为正面，-1 为背面
            float facing : VFACE;
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            
            // 核心 4：法线修正。
            // 如果是背面，我们需要翻转法线，否则内部的光影会看起来是反的。
            // VFACE 在 Surface Shader 中需要特定处理，如果是背面 (facing < 0)，则反转法线。
            float3 normal = float3(0,0,1);
            o.Normal = IN.facing > 0 ? normal : -normal;

            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
        }
        ENDCG
    }
    // 回退到内置 Diffuse 以确保兼容性
    FallBack "Diffuse"
}