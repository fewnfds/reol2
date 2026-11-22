#ifndef REOL2_SURFACE_NOISE_INCLUDED
#define REOL2_SURFACE_NOISE_INCLUDED

float ReolSurfaceHash(int2 cell)
{
    uint h = asuint(cell.x) * 0x9e3779b9u ^ asuint(cell.y) * 0x85ebca6bu;
    h ^= h >> 16;
    h *= 0x7feb352du;
    h ^= h >> 15;
    return (h & 65535u) * (1.0 / 65535.0);
}

float ReolSurfaceNoise(float2 local, int2 origin)
{
    float2 cell = floor(local);
    float2 f = local - cell;
    f = f * f * (3.0 - 2.0 * f);
    int2 id = origin + (int2)cell;
    return lerp(lerp(ReolSurfaceHash(id), ReolSurfaceHash(id + int2(1, 0)), f.x),
        lerp(ReolSurfaceHash(id + int2(0, 1)), ReolSurfaceHash(id + int2(1, 1)), f.x), f.y);
}

#endif
