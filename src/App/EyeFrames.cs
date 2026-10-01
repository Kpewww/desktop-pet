using Aly.Core;
using Aly.Core.Engine;

namespace Aly.App
{
    /// <summary>
    /// Maps the eye controller's state to atlas frames. Eyes are only drawn on frames that
    /// carry the eyes.gaze anchor; expression frames bake their own eyes instead.
    /// </summary>
    sealed class EyeFrames
    {
        readonly Atlas atlas;
        readonly int anchor;
        readonly int openC, openL, openR;
        readonly int[] blink;

        public EyeFrames(Atlas atlas)
        {
            this.atlas = atlas;
            anchor = atlas.AnchorIndex("eyes.gaze");
            openC = First("gaze_open_c");
            openL = First("gaze_open_l");
            openR = First("gaze_open_r");
            AtlasClip b = atlas.GetClip("gaze_blink");
            blink = b == null ? new int[0] : b.Frames;
            if (openL < 0) openL = openC;
            if (openR < 0) openR = openC;
        }

        int First(string clip)
        {
            AtlasClip c = atlas.GetClip(clip);
            return c == null ? -1 : c.Frames[0];
        }

        /// <summary>Eye frame (or -1) and its offset from the canvas, in sprite pixels.</summary>
        public void Resolve(int frame, Eyes eyes, out int eyeFrame, out int dx, out int dy)
        {
            eyeFrame = -1;
            dx = 0;
            dy = 0;
            int ax, ay;
            if (anchor < 0 || openC < 0 || !atlas.TryGetAnchor(frame, anchor, out ax, out ay)) return;
            switch (eyes.Sprite)
            {
                case EyeSprite.OpenLeft: eyeFrame = openL; break;
                case EyeSprite.OpenRight: eyeFrame = openR; break;
                case EyeSprite.Blink:
                    eyeFrame = eyes.BlinkFrame >= 0 && eyes.BlinkFrame < blink.Length ? blink[eyes.BlinkFrame] : openC;
                    break;
                default: eyeFrame = openC; break;
            }
            dx = ax - atlas.OriginX + eyes.OffsetX;
            dy = ay - atlas.OriginY + eyes.OffsetY;
        }
    }
}
