// src/WUM.CLI/Interactive/CommandLineTokenizer.cs
using System.Collections.Generic;
using System.Text;

namespace WUM.CLI.Interactive
{
    /// <summary>
    /// Splits a command line string into argument tokens.
    /// Honors double/single quotes and backslash escapes inside quotes,
    /// so interactive input maps cleanly onto System.CommandLine args.
    /// </summary>
    public static class CommandLineTokenizer
    {
        /// <summary>
        /// Tokenize <paramref name="input"/> into argument tokens.
        /// </summary>
        /// <exception cref="System.FormatException">Thrown on an unmatched quote.</exception>
        public static string[] Tokenize(string input)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(input))
                return tokens.ToArray();

            var current   = new StringBuilder();
            bool inToken   = false;   // are we currently building a token?
            char quote     = '\0';    // active quote char, or '\0' when unquoted

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (quote != '\0')
                {
                    // Inside a quoted span.
                    if (c == '\\' && i + 1 < input.Length)
                    {
                        char next = input[i + 1];
                        // Escape only the active quote or a literal backslash.
                        if (next == quote || next == '\\')
                        {
                            current.Append(next);
                            i++;
                            continue;
                        }
                        current.Append(c);
                        continue;
                    }

                    if (c == quote)
                    {
                        quote = '\0';   // close quote; token may continue
                        continue;
                    }

                    current.Append(c);
                    continue;
                }

                // Outside quotes.
                if (c == '"' || c == '\'')
                {
                    quote   = c;
                    inToken = true;   // empty quotes still produce a token
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (inToken)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                        inToken = false;
                    }
                    continue;
                }

                current.Append(c);
                inToken = true;
            }

            if (quote != '\0')
                throw new System.FormatException(
                    "Unmatched quote (" + quote + "). Close the quote and try again.");

            if (inToken)
                tokens.Add(current.ToString());

            return tokens.ToArray();
        }
    }
}
