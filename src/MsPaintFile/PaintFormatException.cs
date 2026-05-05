namespace MsPaintFile;

public sealed class PaintFormatException : Exception
{
    public PaintFormatException(string message) : base(message) { }
    public PaintFormatException(string message, Exception inner) : base(message, inner) { }
}
