namespace Mzying2001.MonkeySharp.Core
{
    /// <summary>
    /// Interface to verify the script.
    /// </summary>
    public interface IScriptVerifier
    {
        /// <summary>
        /// Verify the script.
        /// </summary>
        /// <param name="script">The script to verify.</param>
        /// <returns>Returns true if the script is valid, otherwise false.</returns>
        bool Verify(string script);


        /// <summary>
        /// Verify the script.
        /// </summary>
        /// <param name="script">The script to verify.</param>
        /// <param name="error">The error message if the script is invalid.</param>
        /// <returns>Returns true if the script is valid, otherwise false.</returns>
        bool Verify(string script, ref string error);
    }
}
