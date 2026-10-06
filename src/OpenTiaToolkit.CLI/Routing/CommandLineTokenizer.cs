using System;
using System.Collections.Generic;
using System.Text;

namespace OpenTiaToolkit.CLI.Routing
{
    /// <summary>
    /// Tokenizes interactive command lines with the same argv shape expected by CommandRouter.
    /// </summary>
    public static class CommandLineTokenizer
    {
        public static bool TryTokenize(string input, out string[] args, out string? error)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            bool tokenStarted = false;

            for (int index = 0; index < input.Length; index++)
            {
                char c = input[index];

                if (c == '\\' && index + 1 < input.Length)
                {
                    char next = input[index + 1];
                    if (next == '"' || next == '\\')
                    {
                        current.Append(next);
                        tokenStarted = true;
                        index++;
                        continue;
                    }
                }

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    tokenStarted = true;
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (tokenStarted)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                        tokenStarted = false;
                    }

                    continue;
                }

                current.Append(c);
                tokenStarted = true;
            }

            if (inQuotes)
            {
                args = Array.Empty<string>();
                error = "Unbalanced quote in command line.";
                return false;
            }

            if (tokenStarted)
            {
                tokens.Add(current.ToString());
            }

            args = tokens.ToArray();
            error = null;
            return true;
        }
    }
}
