using LabelForge.Core.Model;

namespace LabelForge.Core.Editing;

public readonly record struct DrawTarget(int X, int Y, int Width, int Height, bool Left, bool Top,
    Orientation? Rotation);

public static class DrawGesture
{
    public static DrawTarget Snap(Element element, DrawTarget target, int anchorX, int anchorY,
        IReadOnlyList<int> targetsX, IReadOnlyList<int> targetsY, int threshold,
        bool keepAspect, out int? snapX, out int? snapY)
    {
        int edgeX = target.Left ? target.X : target.X + target.Width;
        int edgeY = target.Top ? target.Y : target.Y + target.Height;
        (int shiftX, int? targetX) = GuideSnapper.Snap(edgeX, edgeX,
            element is LineElement && target.Rotation == Orientation.Rotated90 ? [] : targetsX, threshold);
        (int shiftY, int? targetY) = GuideSnapper.Snap(edgeY, edgeY,
            element is LineElement && target.Rotation == Orientation.Normal ? [] : targetsY, threshold);
        snapX = targetX;
        snapY = targetY;
        int width = Math.Max(target.Width + (target.Left ? -shiftX : shiftX), 1);
        int height = Math.Max(target.Height + (target.Top ? -shiftY : shiftY), 1);
        if (keepAspect)
        {
            if (snapX is not null && (snapY is null || Math.Abs(shiftX) <= Math.Abs(shiftY)))
            {
                height = Math.Max((int)Math.Round((double)width * target.Height / target.Width), 1);
                snapY = null;
            }
            else if (snapY is not null)
            {
                width = Math.Max((int)Math.Round((double)height * target.Width / target.Height), 1);
                snapX = null;
            }
        }

        return target with { X = target.Left ? anchorX - width : anchorX,
            Y = target.Top ? anchorY - height : anchorY, Width = width, Height = height };
    }

    public static DrawTarget Calculate(Element element, double startX, double startY,
        double x, double y, bool constrain)
    {
        double dx = x - startX;
        double dy = y - startY;
        double width = Math.Abs(dx);
        double height = Math.Abs(dy);
        bool left = dx < 0;
        bool top = dy < 0;
        Orientation? rotation = null;
        if (element is LineElement line)
        {
            bool vertical = height > width;
            rotation = vertical ? Orientation.Rotated90 : Orientation.Normal;
            if (vertical)
            {
                width = line.ThicknessDots;
                left = false;
            }
            else
            {
                height = line.ThicknessDots;
                top = false;
            }
        }
        else
        {
            if (constrain && element is BoxElement or EllipseElement or DiagonalLineElement)
            {
                width = height = Math.Max(width, height);
            }
            else if (constrain && element is ImageElement image)
            {
                double ratio = (double)Math.Max(image.SourcePixelWidth, 1) / Math.Max(image.SourcePixelHeight, 1);
                width = Math.Max(width, height * ratio);
                height = width / ratio;
            }

            if (element is DiagonalLineElement)
            {
                rotation = left == top ? Orientation.Rotated90 : Orientation.Normal;
            }
        }

        return new DrawTarget((int)Math.Round(left ? startX - width : startX),
            (int)Math.Round(top ? startY - height : startY),
            Math.Max((int)Math.Round(width), 1), Math.Max((int)Math.Round(height), 1),
            left, top, rotation);
    }
}
