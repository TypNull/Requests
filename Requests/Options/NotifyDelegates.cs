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

    /// <summary>
    /// Generic delegate has no return type but two generic parameter;
    /// </summary>
    /// <typeparam name="T0">Can be everything</typeparam>
    /// <typeparam name="T1">Can be everything</typeparam>
    /// <param name="element0">Genreic element</param>
    /// <param name="element1">Genreic element</param>
    public delegate void Notify<T0,T1>(T0? element0, T1? element1);
}
