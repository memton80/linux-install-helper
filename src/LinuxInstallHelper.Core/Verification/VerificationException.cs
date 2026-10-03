namespace LinuxInstallHelper.Core.Verification;

public enum VerificationFailure
{
    /// <summary>The SHA-256 of the ISO is not the published one.</summary>
    ChecksumMismatch,

    /// <summary>An OpenPGP signature exists but is not valid for the pinned keys.</summary>
    BadSignature,

    /// <summary>The ISO is not listed in the checksum file.</summary>
    NotListed,

    /// <summary>The checksum information could not be obtained.</summary>
    ChecksumUnavailable,
}

/// <summary>The image could not be proven authentic: the write must not happen.</summary>
public sealed class VerificationException : Exception
{
    public VerificationException(VerificationFailure failure, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
    }

    public VerificationFailure Failure { get; }
}
