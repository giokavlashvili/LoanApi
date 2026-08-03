namespace Application.Common.Serialization
{
    /// <summary>
    /// Serializes a verifiable operation's payload with every <c>[SensitiveData]</c> property
    /// encrypted, and reads it back decrypted. One interface for both directions on purpose —
    /// they are the same converter run two ways, so there is no version of this where encryption
    /// is added and decryption is forgotten.
    /// </summary>
    public interface IProtectedPayloadSerializer
    {
        string Serialize(object? value, Type type);

        object? Deserialize(string json, Type type);
    }
}
