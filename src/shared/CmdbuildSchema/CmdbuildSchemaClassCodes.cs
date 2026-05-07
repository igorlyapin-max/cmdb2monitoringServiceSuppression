using System.Globalization;
using System.Text;

namespace Cmdb2MonitoringServiceSuppression.Shared.CmdbuildSchema;

public static class CmdbuildSchemaClassCodes
{
    public static IReadOnlyList<CmdbuildModelRootClassCode> ModelRootClassCodes(
        string prefix,
        BuilderLayer layer,
        string rootPath)
    {
        var segments = NormalizeRootPath(rootPath)
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return [];
        }

        var result = new List<CmdbuildModelRootClassCode>();
        _ = layer;
        var accumulator = "";
        var parentClassCode = "";
        var displayPath = new List<string>();

        foreach (var segment in segments)
        {
            displayPath.Add(segment);
            var segmentCode = NormalizeClassSegment(segment);
            accumulator = string.IsNullOrWhiteSpace(accumulator)
                ? segmentCode
                : accumulator + segmentCode;
            var code = ApplyPrefix(prefix, accumulator);
            result.Add(new CmdbuildModelRootClassCode(
                Code: code,
                DisplayName: segment,
                ParentClassCode: parentClassCode,
                RootPath: "/" + string.Join('/', displayPath)));
            parentClassCode = code;
        }

        return result;
    }

    public static string NormalizeRootPath(string rootPath)
    {
        var normalized = (rootPath ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "";
        }

        normalized = normalized.Replace('\\', '/').Trim('/');
        return string.IsNullOrWhiteSpace(normalized)
            ? ""
            : "/" + normalized;
    }

    private static string ApplyPrefix(string prefix, string code)
    {
        var normalizedPrefix = (prefix ?? "").Trim();
        return string.IsNullOrWhiteSpace(normalizedPrefix)
            || code.StartsWith(normalizedPrefix, StringComparison.Ordinal)
                ? code
                : normalizedPrefix + code;
    }

    private static string NormalizeClassSegment(string segment)
    {
        var transliterated = Transliterate(segment ?? "");
        var builder = new StringBuilder();
        var capitalizeNext = true;

        foreach (var character in transliterated)
        {
            if (!char.IsLetterOrDigit(character))
            {
                capitalizeNext = true;
                continue;
            }

            builder.Append(capitalizeNext
                ? char.ToUpperInvariant(character)
                : character);
            capitalizeNext = false;
        }

        var code = builder.ToString();
        if (string.IsNullOrWhiteSpace(code))
        {
            return "Root";
        }

        return char.IsLetter(code[0]) ? code : "Root" + code;
    }

    private static string Transliterate(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormC))
        {
            builder.Append(character switch
            {
                'А' or 'а' => "a",
                'Б' or 'б' => "b",
                'В' or 'в' => "v",
                'Г' or 'г' => "g",
                'Д' or 'д' => "d",
                'Е' or 'е' or 'Ё' or 'ё' => "e",
                'Ж' or 'ж' => "zh",
                'З' or 'з' => "z",
                'И' or 'и' => "i",
                'Й' or 'й' => "y",
                'К' or 'к' => "k",
                'Л' or 'л' => "l",
                'М' or 'м' => "m",
                'Н' or 'н' => "n",
                'О' or 'о' => "o",
                'П' or 'п' => "p",
                'Р' or 'р' => "r",
                'С' or 'с' => "s",
                'Т' or 'т' => "t",
                'У' or 'у' => "u",
                'Ф' or 'ф' => "f",
                'Х' or 'х' => "h",
                'Ц' or 'ц' => "ts",
                'Ч' or 'ч' => "ch",
                'Ш' or 'ш' => "sh",
                'Щ' or 'щ' => "sch",
                'Ы' or 'ы' => "y",
                'Э' or 'э' => "e",
                'Ю' or 'ю' => "yu",
                'Я' or 'я' => "ya",
                'Ь' or 'ь' or 'Ъ' or 'ъ' => "",
                _ when CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark => "",
                _ => character.ToString()
            });
        }

        return builder.ToString();
    }
}

public sealed record CmdbuildModelRootClassCode(
    string Code,
    string DisplayName,
    string ParentClassCode,
    string RootPath);
