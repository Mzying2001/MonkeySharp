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

                if (escapeNext) // Escape character
                {
                    escapeNext = false;
                    continue;
                }

                if (inSingleLineComment) // Single-line comment
                {
                    if (current == '\n')
                        inSingleLineComment = false;
                }
                else if (inMultiLineComment) // Multi-line comment
                {
                    if (current == '*' && i + 1 < script.Length && script[i + 1] == '/')
                    {
                        inMultiLineComment = false;
                        i++;
                    }
                }
                else if (inString != StringState.None) // Inside a string
                {
                    if (current == '\\')
                    {
                        escapeNext = true;
                    }
                    else if (inString == StringState.Template &&
                        current == '$' && i + 1 < script.Length && script[i + 1] == '{')
                    {
                        templateStack.Push(new TemplateState
                        {
                            bracketCount = bracketStack.Count,
                            stringState = inString
                        });
                        inString = StringState.None;
                        i++;
                    }
                    else
                    {
                        if ((inString == StringState.Single && current == '\'') ||
                            (inString == StringState.Double && current == '"') ||
                            (inString == StringState.Template && current == '`'))
                        {
                            inString = StringState.None;
                        }
                    }
                }
                else // Outside strings and comments
                {
                    if (current == '/' && i + 1 < script.Length)
                    {
                        char nextChar = script[i + 1];
                        if (nextChar == '/')
                        {
                            inSingleLineComment = true;
                            i++;
                        }
                        else if (nextChar == '*')
                        {
                            inMultiLineComment = true;
                            i++;
                        }
                    }
                    else
                    {
                        switch (current)
                        {
                            case '\'':
                                inString = StringState.Single;
                                break;

                            case '"':
                                inString = StringState.Double;
                                break;

                            case '`':
                                inString = StringState.Template;
                                break;

                            case '(':
                            case '[':
                            case '{':
                                bracketStack.Push(current);
                                break;

                            case ')':
                            case ']':
                            case '}':
                                {
                                    if (current == '}' && templateStack.Count > 0
                                        && templateStack.Peek().bracketCount == bracketStack.Count)
                                    {
                                        inString = templateStack.Pop().stringState;
                                    }
                                    else if (bracketStack.Count == 0)
                                    {
                                        error = $"Unmatched closing bracket '{current}' at position {i}";
                                        return false;
                                    }
                                    else
                                    {
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
                                break;

                            default:
                                break;
                        } // End switch
                    }
                } // End if
            }

            if (bracketStack.Count > 0)
            {
                error = "Unmatched opening bracket(s): " + string.Join(", ", bracketStack) + ".";
                return false;
            }

            if (/*inSingleLineComment ||*/ inMultiLineComment)
            {
                error = "Unclosed comment.";
                return false;
            }

            if (inString != StringState.None)
            {
                error = "Unclosed string or template string.";
                return false;
            }

            if (templateStack.Count > 0)
            {
                error = "Unclosed template string code block.";
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
                default: throw new ArgumentException("Invalid bracket character.");
            }
        }


        /// <summary>
        /// The global instance.
        /// </summary>
        public static DefScriptVerifier Instance { get; } = new DefScriptVerifier();
    }
}
