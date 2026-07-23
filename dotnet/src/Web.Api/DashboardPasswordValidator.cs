using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;

namespace Web.Api;

public class DashboardPasswordValidator(
    DashboardAuthenticationOptions options,
    IHostEnvironment hostEnvironment)
{
    public bool IsValid(string password) =>
        options.IsBypassed(hostEnvironment) || PasswordsMatch(password, options.Password);

    private static bool PasswordsMatch(string supplied, string expected)
    {
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return suppliedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}
