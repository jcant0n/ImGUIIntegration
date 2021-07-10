struct PS_IN
{
    float4 pos : SV_POSITION;
    float4 col : COLOR0;
    float2 uv  : TEXCOORD0;
};

Texture2D FontTexture : register(t0);
sampler FontSampler : register(s0);

float4 PS(PS_IN input) : SV_Target
{
    float4 out_col = input.col * FontTexture.Sample(FontSampler, input.uv);
    return out_col;
}