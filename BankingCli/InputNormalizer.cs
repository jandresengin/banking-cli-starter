using System.Text.RegularExpressions;

namespace BankingCli;

/// <summary>Maps common account aliases and Spanish/French banking terms before interpretation.</summary>
internal static class InputNormalizer
{
    public static (string Text, bool NeedsConfirmation) Normalize(string input)
    {
        string text = input;
        bool spanish = Regex.IsMatch(text, @"\b(?:retira|retirar|deposita|depositar|transfiere|transferir|mueve|ahorros?|corriente|cuenta|d[oó]lares|saldo)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        bool french = Regex.IsMatch(text, @"\b(?:retire|retirer|retrait|d[ée]pose|d[ée]poser|d[ée]p[oô]t|transf[èe]re|transf[ée]rer|virement|[ée]pargne|courant|compte|solde)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (spanish)
        {
            text = Replace(text, @"\bcuenta\s+(?:de\s+)?ahorros?\b", "savings");
            text = Replace(text, @"\bcuenta\s+(?:de\s+)?(?:cheques|corriente)\b", "chequing");
            text = Replace(text, @"\bahorros?\b", "savings");
            text = Replace(text, @"\b(?:corriente|cheques)\b", "chequing");
            text = Replace(text, @"\b(?:retira|retirar)\b", "withdraw");
            text = Replace(text, @"\b(?:deposita|depositar)\b", "deposit");
            text = Replace(text, @"\b(?:transfiere|transferir)\b", "transfer");
            text = Replace(text, @"\bmueve\b", "move");
            text = Replace(text, @"\bsaldo(?:s)?\b", "balance");
            text = Replace(text, @"\bd[oó]lares\b", "dollars");
            text = Replace(text, @"\b(?:de|desde)\s+(?:la\s+)?", "from ");
            text = Replace(text, @"\ba\s+(?:la\s+)?", "to ");
            text = Replace(text, @"\b(?:la|el|las|los|mi|mis)\b", " ");
        }

        if (french)
        {
            text = Replace(text, @"\bcompte\s+(?:d['’]|de\s+)?[ée]pargne\b", "savings");
            text = Replace(text, @"\bcompte\s+(?:de\s+)?(?:ch[eè]ques?|courant)\b", "chequing");
            text = Replace(text, @"\b[ée]pargne\b", "savings");
            text = Replace(text, @"\b(?:ch[eè]ques?|courant)\b", "chequing");
            text = Replace(text, @"\b(?:retire|retirer|retrait)\b", "withdraw");
            text = Replace(text, @"\b(?:d[ée]pose|d[ée]poser|d[ée]p[oô]t)\b", "deposit");
            text = Replace(text, @"\b(?:transf[èe]re|transf[ée]rer|virement)\b", "transfer");
            text = Replace(text, @"\b(?:solde|soldes)\b", "balance");
            text = Replace(text, @"\b(?:depuis|du)\b", "from");
            text = Replace(text, @"\bde\s+(?:mon\s+|ma\s+|la\s+|le\s+)?", "from ");
            text = Replace(text, @"(?:\b(?:vers|dans|sur)\b|(?<!\w)à(?!\w))\s+(?:mon\s+|ma\s+|le\s+|la\s+)?", "to ");
            text = Replace(text, @"\b(?:mon|ma|mes|le|la|les|un|une)\b", " ");
        }

        bool alias = Regex.IsMatch(text, @"\bsaving\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = Replace(text, @"\bsaving\b", "savings");
        bool typo = Regex.IsMatch(text, @"\b(?:depsoit|deopsit|savngs|cheq(u)?ingg)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return (text, spanish || french || alias || typo);
    }

    private static string Replace(string text, string pattern, string replacement) =>
        Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
