#ifndef TIME_SKILL_COLOR_INCLUDED
#define TIME_SKILL_COLOR_INCLUDED

float _StageGrayscale;
float4 _SkillWaveOrigin;
float _SkillWaveRadius;
float _SkillWaveComplete;

// 조명·비조명 스프라이트가 같은 파동과 위험물 색상 계산을 공유한다.
half4 ApplyTimeSkillColor(half4 color, float2 worldPosition, half dangerous)
{
    float distanceFromOrigin = distance(worldPosition, _SkillWaveOrigin.xy);
    float wave = 1.0 - smoothstep(_SkillWaveRadius - 0.35, _SkillWaveRadius, distanceFromOrigin);
    wave *= step(0.0001, _SkillWaveRadius);
    half strength = saturate(_StageGrayscale) * max(wave, _SkillWaveComplete);
    half gray = dot(color.rgb, half3(0.299h, 0.587h, 0.114h));
    half3 dangerColor = half3(max(gray, 0.18h), gray * 0.12h, gray * 0.1h);
    half3 target = lerp(gray.xxx, dangerColor, saturate(dangerous));
    color.rgb = lerp(color.rgb, target, strength);
    return color;
}

#endif
