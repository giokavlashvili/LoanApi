namespace Application.Common.Confirmation
{
    /// <summary>
    /// Who may confirm an operation, relative to whoever initiated it.
    /// </summary>
    public enum ConfirmationPolicy
    {
        /// <summary>
        /// Only the initiator. The default, and the only safe choice for a command that derives
        /// anything from the ambient identity.
        /// <para>
        /// When the captured command is replayed, <c>ICurrentUserService.UserId</c> is the
        /// <b>confirmer</b>, not the initiator. For an approval that is exactly right — the
        /// approver is whose authority matters, and the audit interceptor stamping them is what
        /// you want. For a command that records ownership from the ambient user it is wrong: the
        /// record would be attributed to whoever approved it. Under this policy the two are the
        /// same principal, so the question does not arise, which is why it is the default.
        /// </para>
        /// </summary>
        SameUser = 0,

        /// <summary>Any authenticated user. Rarely what you want; state a reason when using it.</summary>
        AnyAuthorized,

        /// <summary>
        /// Anyone except the initiator — four eyes. Only for commands whose handler does not
        /// derive ownership from the ambient identity; see <see cref="SameUser"/>.
        /// </summary>
        DifferentUser
    }
}
