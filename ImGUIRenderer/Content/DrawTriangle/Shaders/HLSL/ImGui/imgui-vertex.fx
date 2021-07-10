cbuffer Matrices : register(b0)
{
    float4x4 ProjectionMatrix;
};

struct VS_IN
{
    float2 pos : POSITION;
    float2 uv  : TEXCOORD0;
    float4 col : COLOR0;
};

struct PS_IN
{
    float4 pos : SV_POSITION;
    float4 col : COLOR0;
    float2 uv  : TEXCOORD0;
};

PS_IN VS(VS_IN input)
{
    PS_IN output;
    output.pos = mul(float4(input.pos.xy, 0.f, 1.f), ProjectionMatrix);
    output.col = input.col;
    output.uv = input.uv;
    return output;
}
