using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AuraToolsExp.Dll.Features.Feast;

public static class FeastRoleResourceIdentity
{
    public static string FolderName(string roleId)
    {
        var normalized = (roleId ?? "").Trim();
        var slug = new string(normalized
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_')
            .ToArray())
            .Trim('_');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "role";
        }

        if (slug.Length > 72)
        {
            slug = slug.Substring(0, 72).TrimEnd('_');
        }

        return slug + "--" + Hash(normalized, 10).ToLowerInvariant();
    }

    public static string CgId(string roleId)
    {
        return "generated.feast." + Hash((roleId ?? "").Trim(), 16).ToLowerInvariant();
    }

    private static string Hash(string value, int length)
    {
        using var sha = SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value.ToLowerInvariant())))
            .Replace("-", "");
        return hash.Substring(0, Math.Min(length, hash.Length));
    }
}
