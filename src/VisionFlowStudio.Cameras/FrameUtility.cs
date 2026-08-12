using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using VisionFlowStudio.Core;

namespace VisionFlowStudio.Cameras
{
    internal static class FrameUtility
    {
        public static CameraFrameData FromBitmap(Bitmap source)
        {
            using (var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(bitmap)) g.DrawImage(source, 0, 0, source.Width, source.Height);
                var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    var pixels = new byte[Math.Abs(data.Stride) * bitmap.Height];
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                    return new CameraFrameData { Width = bitmap.Width, Height = bitmap.Height, Stride = Math.Abs(data.Stride), BgrPixels = pixels, Timestamp = DateTime.Now };
                }
                finally { bitmap.UnlockBits(data); }
            }
        }
    }
}
