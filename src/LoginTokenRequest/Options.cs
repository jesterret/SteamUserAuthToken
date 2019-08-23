using CommandLine;

namespace LoginTokenRequest
{
    internal class Options
    {
        [Option('u', Required = true, HelpText = "User account login.")]
        public string Username { get; set; }

        [Option('p', Required = true, HelpText = "User account password.")]
        public string Password { get; set; }

        [Option("id", Default = null, HelpText = "Can be used for multiple running sessions from the same machine.")]
        public uint? LoginId { get; set; }
    }
}