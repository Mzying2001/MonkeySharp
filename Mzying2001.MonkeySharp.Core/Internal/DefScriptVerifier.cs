using System.Collections.Generic;
using System;

namespace Mzying2001.MonkeySharp.Core.Internal
{
    /// <summary>
    /// The default script verifier, used to prevent sandbox escape.
    /// </summary>
    internal class DefScriptVerifier : IScriptVerifier
    {
        private enum StringState
        {
            None,
            Single,
            Double,
            Template
        }


        private struct TemplateState
        {
            public int bracketCount;
            public StringState stringState;
        }


        /// <inheritdoc />
        public bool Verify(string script)
        {
            string err = null;
            return Verify(script, ref err);
        }


        /// <inheritdoc />
        public bool Verify(string script, ref string error)
        {
            var bracketStack = new Stack<char>();
            var templateStack = new Stack<TemplateState>();

            StringState inString = StringState.None;
            bool inSingleLineComment = false;
            bool inMultiLineComment = false;
            bool escapeNext = false;

            for (int i = 0; i < script.Length; i++)
            {
                char current = script[i];

                if (escapeNext)
                {
                    escapeNext = false;
                    continue;
                }

                if (inSingleLineComment)
                {
                    if (current == '\n')
                        inSingleLineComment = false;
                    continue;
                }
                else if (inMultiLineComment)
                {
                    if (current == '*' && i + 1 < script.Length && script[i + 1] == '/')
                    {
                        inMultiLineComment = false;
                        i++;
                    }
                    continue;
                }
                else
                {
                    if (inString == StringState.None && current == '/' && i + 1 < script.Length)
                    {
                        char nextChar = script[i + 1];
                        if (nextChar == '/')
                        {
                            inSingleLineComment = true;
                            i++;
                            continue;
                        }
                        else if (nextChar == '*')
                        {
                            inMultiLineComment = true;
                            i++;
                            continue;
                        }
                    }
                }

                if (inString != StringState.None)
                {
                    if (current == '\\')
                    {
                        escapeNext = true;
                        continue;
                    }

                    if ((inString == StringState.Single && current == '\'') ||
                        (inString == StringState.Double && current == '"') ||
                        (inString == StringState.Template && current == '`'))
                    {
                        inString = StringState.None;
                        continue;
                    }

                    if (inString == StringState.Template && current == '$' && i + 1 < script.Length && script[i + 1] == '{')
                    {
                        templateStack.Push(new TemplateState
                        {
                            bracketCount = bracketStack.Count,
                            stringState = inString
                        });
                        inString = StringState.None;
                        i++;
                        continue;
                    }

                    continue;
                }
                else
                {
                    if (current == '\'')
                    {
                        inString = StringState.Single;
                    }
                    else if (current == '"')
                    {
                        inString = StringState.Double;
                    }
                    else if (current == '`')
                    {
                        inString = StringState.Template;
                    }
                    else
                    {
                        if (current == '(' || current == '[' || current == '{')
                        {
                            bracketStack.Push(current);
                        }
                        else if (current == ')' || current == ']' || current == '}')
                        {
                            if (current == '}' && templateStack.Count > 0
                                && templateStack.Peek().bracketCount == bracketStack.Count)
                            {
                                inString = templateStack.Pop().stringState;
                                continue;
                            }

                            if (bracketStack.Count == 0)
                            {
                                error = $"Unmatched closing bracket '{current}' at position {i}";
                                return false;
                            }

                            char top = bracketStack.Peek();
                            bool isMatching = (current == ')' && top == '(') ||
                                              (current == ']' && top == '[') ||
                                              (current == '}' && top == '{');

                            if (!isMatching)
                            {
                                error = $"Mismatched closing bracket '{current}' at position {i}. Expected '{GetMatchingBracket(top)}'.";
                                return false;
                            }

                            bracketStack.Pop();
                        }
                    }
                }
            }

            if (bracketStack.Count > 0)
            {
                error = "Unmatched opening bracket(s): " + string.Join(", ", bracketStack);
                return false;
            }

            if (/*inSingleLineComment ||*/ inMultiLineComment)
            {
                error = "Unclosed comment";
                return false;
            }

            if (inString != StringState.None)
            {
                error = "Unclosed string or template string";
                return false;
            }

            if (templateStack.Count > 0)
            {
                error = "Unclosed template string code block";
                return false;
            }

            return true;
        }


        private static char GetMatchingBracket(char bracket)
        {
            switch (bracket)
            {
                case '(': return ')';
                case '[': return ']';
                case '{': return '}';
                default: throw new ArgumentException("Invalid bracket character");
            }
        }


        /// <summary>
        /// The global instance.
        /// </summary>
        public static DefScriptVerifier Instance { get; } = new DefScriptVerifier();
    }
}
