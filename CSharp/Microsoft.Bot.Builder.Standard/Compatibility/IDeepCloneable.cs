namespace Microsoft.Bot.Builder.Compatibility
{
    public interface IDeepCloneable
    {
        /// <summary>
        /// Deeply clones this instance.
        /// </summary>
        object Clone();
    }
}
