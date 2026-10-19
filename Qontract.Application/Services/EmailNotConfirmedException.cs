namespace Qontract.Application.Services;
// Thrown only after the password was verified, so it reveals nothing to someone without it.
public class EmailNotConfirmedException() : InvalidOperationException("Please confirm your email address before signing in.");
