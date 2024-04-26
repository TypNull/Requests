namespace Requests.Options
{
    /// <summary>
    /// Represents a void type.
    /// </summary>
    public struct VoidStruct { }

    /// <summary>
    /// Represents a delegate with no return type or parameters.
    /// </summary>
    public delegate void NotifyVoid();

    /// <summary>
    /// Represents a generic delegate with no return type but a single generic parameter.
    /// </summary>
    /// <typeparam name="T">Can be any type.</typeparam>
    /// <param name="element">Generic element.</param>
    public delegate void Notify<T>(T? element);

    /// <summary>
    /// Represents a generic delegate with no return type but two generic parameters.
    /// </summary>
    /// <typeparam name="T0">Can be any type.</typeparam>
    /// <typeparam name="T1">Can be any type.</typeparam>
    /// <param name="element0">Generic element.</param>
    /// <param name="element1">Generic element.</param>
    public delegate void Notify<T0, T1>(T0? element0, T1? element1);
}
