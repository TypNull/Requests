namespace Requests.Options
{
    /// <summary>
    /// Struct as void
    /// </summary>
    public struct VoidStruct { }

    /// <summary>
    /// Delegate has no return type or parameter;
    /// </summary>
    public delegate void NotifyVoid();

    /// <summary>
    /// Generic delegate has no return type but a generic parameter;
    /// </summary>
    /// <typeparam name="T">Can be everything</typeparam>
    /// <param name="element">Genreic element</param>
    public delegate void Notify<T>(T? element);
}
