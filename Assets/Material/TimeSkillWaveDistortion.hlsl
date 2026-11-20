#ifndef GAME_TIME_SKILL_WAVE_DISTORTION_INCLUDED
#define GAME_TIME_SKILL_WAVE_DISTORTION_INCLUDED

// xy: radial displacement/width, z: wavelength, w: remaining propagation.
float4 _StageWaveDistortion;

float2 StageWaveDistortedUV(float2 uv, float2 worldPosition)
{
    // 월드 변위를 픽셀 미분으로 UV 변위에 옮겨 회전·확대·반전을 반영한다.
    float2 worldDx = ddx(worldPosition);
    float2 worldDy = ddy(worldPosition);
    float2 uvDx = ddx(uv);
    float2 uvDy = ddy(uv);
    float determinant = worldDx.x * worldDy.y - worldDx.y * worldDy.x;
    float safeDeterminant = determinant < 0.0 ? -max(abs(determinant), 1e-8) : max(abs(determinant), 1e-8);

    float2 radial = worldPosition - _StageWave.xy;
    float radialDistance = length(radial);
    float distanceToFront = radialDistance - _StageWave.z;
    float width = max(0.01, _StageWaveDistortion.y);
    float normalizedDistance = distanceToFront / width;
    float envelope = exp(-normalizedDistance * normalizedDistance * 3.0);
    float oscillation = sin(distanceToFront * 6.283185 / max(0.01, _StageWaveDistortion.z));
    float amplitude = _StageWaveDistortion.x * envelope * oscillation;
    amplitude *= saturate(_StageGrayscale) * _StageWave.w * _StageWaveDistortion.w;
    float2 displacement = radial / max(radialDistance, 0.0001) * amplitude;

    float2 pixelOffset = float2(displacement.x * worldDy.y - displacement.y * worldDy.x,
        worldDx.x * displacement.y - worldDx.y * displacement.x) / safeDeterminant;
    float2 distortedUV = uv + uvDx * pixelOffset.x + uvDy * pixelOffset.y;
    // 원본 그림과 충돌은 유지하고 같은 스프라이트 안에서 샘플 위치만 옮긴다.
    float2 boundedUV = clamp(distortedUV, _StageSpriteUVBounds.xy, _StageSpriteUVBounds.zw);
    return lerp(uv, boundedUV, step(1e-10, dot(displacement, displacement)));
}

#endif
