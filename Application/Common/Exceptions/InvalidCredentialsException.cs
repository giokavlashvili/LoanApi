namespace Application.Common.Exceptions
{
    /// <summary>
    /// Authentication failed. Maps to <b>401</b>, not the 404 this used to raise: a wrong password
    /// is not a missing resource, and telling a client "not found" points it at the wrong recovery.
    /// <para>
    /// Deliberately carries no detail about <em>which</em> half failed. The same exception and the
    /// same message are used for an unknown user name and for a wrong password, so the response
    /// cannot be used to enumerate accounts.
    /// </para>
    /// </summary>
    public class InvalidCredentialsException : Exception
    {
        /// <summary>The message is a localization key, resolved by the WebApi exception filter.</summary>
        public InvalidCredentialsException()
            : base("InvalidCredentials")
        {
        }

        public InvalidCredentialsException(string message)
            : base(message)
        {
        }

        public InvalidCredentialsException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
