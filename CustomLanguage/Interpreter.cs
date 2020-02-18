using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomLanguage
{
    public class Interpreter
    {
        private string[] lines;
        private Dictionary<string, float> variables;
        private bool ifStatementMet;

        private readonly Dictionary<string, float> startingVariables = new Dictionary<string, float>()
        {
            {"pi", 3.14159f},
            {"e", 2.71828f}
        };

        private const char arrayDelimiter = '$';
        private readonly string[] reservedNames = { "if", "else", "elif", "while", "Sin", "Asin", "Sign", "Sqrt", "Log", };

        private readonly Regex bracketRegex = new Regex(@"
            \(                    # Match (
            (
            [^()]+            # all chars except ()
            | (?<Level>\()    # or if ( then Level += 1
            | (?<-Level>\))   # or if ) then Level -= 1
            )+                    # Repeat (to go from inside to outside)
            (?(Level)(?!))        # zero-width negative lookahead assertion
            \)                    # Match )", RegexOptions.IgnorePatternWhitespace);

        private readonly Regex squareBracketRegex = new Regex(@"\[(.*?)\]");
        private readonly Regex variableNameRegex = new Regex(@"^[\d_]*[a-zA-Z][a-zA-Z\d_]*$");
        private readonly Regex arrayNameRegex = new Regex(@"^[\d_]*[a-zA-Z][a-zA-Z\d_\$]*$");

        public Interpreter(string rawCode)
        {
            //Remove all spaces and tabs
            rawCode = rawCode.Replace(" ", string.Empty);
            rawCode = rawCode.Replace("\t", string.Empty);

            //Ensure all curly braces are on their own line
            rawCode = rawCode.Replace("{", "\n{\n");
            rawCode = rawCode.Replace("}", "\n}\n");

            //Split into lines array based on carriage returns and semi colons
            lines = Regex.Split(rawCode, "\r\n|\r|\n|;");

            //Add Starting Variables
            variables = startingVariables;


            ifStatementMet = false;
            //Start interpreting
            InterpretLines(0, lines.Length - 1);
        }

        public void SetVariable(string name, float value, bool arrayItem = false)
        {
            bool nameProblem = false;

            //Check characters (only allow a-z,A-Z, 0-9 and _
            if (!variableNameRegex.Match(name).Success && !arrayItem)
            {
                nameProblem = true;
                OutputErrorMessage("Variable name can only contain 'a-z', 'A-Z', '0-9' and '_'. Name " + name + " is invalid.");
            }
            else if (!arrayNameRegex.Match(name).Success && arrayItem) //If an array item also allow $, but not as first letter (i.e. must have a name)
            {
                nameProblem = true;
                OutputErrorMessage("array name can only contain 'a-z', 'A-Z', '0-9' and '_'. Name " + name + " is invalid.");
            }

            //Need to check against reserved words  
            foreach (string reservedWord in reservedNames)
            {
                if (Regex.Match(name, reservedWord).Success)
                {
                    nameProblem = true;
                    OutputErrorMessage("Variable name cannot contain the reserved word " + reservedWord);
                }
            }

            //Add new variable if it doesn't exist
            if (!variables.ContainsKey(name) && !nameProblem)
            {
                variables.Add(name, value);
                OutputMessage("Added New Variable " + name + " with value " + value.ToString());
            }

            //If it does exist then update it's value
            else if (variables.ContainsKey(name) && !nameProblem)
            {
                variables[name] = value;
                OutputMessage("Updated Variable " + name + " with new value " + value.ToString());
            }
        }

        public float GetVariable(string name)
        {
            if (variables.ContainsKey(name))
            {
                return variables[name];
            }
            else
            {
                OutputErrorMessage("Error, could not find variable with name " + name);
                return 0;
            }
        }

        /// <summary>
        /// This function is designed to convert an external c# array to one in the language here, not designed for internal use
        /// </summary>
        public void InitialiseArray(string name, float[] values)
        {
            int i = 0;
            foreach (float val in values)
            {
                string itemName = name + arrayDelimiter + i.ToString();
                SetVariable(itemName, val, true);
            }
        }

        /// <summary>
        /// Used to internally append new elements or update existing ones
        /// </summary>
        public void UpdateArray(string name, int index, float value)
        {
            string itemName = name + arrayDelimiter + index.ToString();
            SetVariable(itemName, value, true);
        }

        /// <summary>
        /// Used to fetch item from an arry, either internally or externally
        public float GetArrayItem(string name, int index)
        {
            string itemName = name + arrayDelimiter + index.ToString();
            return GetVariable(itemName);
        }

        private void InterpretLines(int currentLineIndex, int endIndex)
        {
            string currentLine = lines[currentLineIndex];

            string[] sections = Regex.Split(currentLine, @"((?<!=|!)=(?!=)|{|})");

            foreach (string section in sections)
            {
                if (section == "=")
                {
                    //Handle Assignment
                    HandleAssignment(currentLine);
                }
                else if (section.StartsWith("if"))
                {
                    bool statementTruth = HandleConditional(currentLine);

                    //If false then set currentLineIndex to the closing parenthesis
                    //Else carry on as normal

                    if (!statementTruth)
                    {
                        ifStatementMet = false;
                        currentLineIndex = FindEndIndex(currentLineIndex);
                    }
                    else
                    {
                        ifStatementMet = true;
                    }
                }
                else if (section.StartsWith("elif"))
                {

                    if (!ifStatementMet)
                    {
                        bool statementTruth = HandleConditional(currentLine);
                        if (!statementTruth)
                        {
                            ifStatementMet = false;
                            currentLineIndex = FindEndIndex(currentLineIndex);
                        }
                        else
                        {
                            ifStatementMet = true;
                        }
                    }
                    else
                    {
                        currentLineIndex = FindEndIndex(currentLineIndex);
                    }
                }
                else if (section.StartsWith("else"))
                {

                    if (ifStatementMet)
                    {
                        currentLineIndex = FindEndIndex(currentLineIndex);
                    }
                }
                else if (section.StartsWith("while"))
                {
                    int loopEndingLine = FindEndIndex(currentLineIndex);

                    while (HandleConditional(currentLine))
                    {
                        InterpretLines(currentLineIndex + 1, loopEndingLine);
                    }
                    currentLineIndex = loopEndingLine;
                }
            }
            if (currentLineIndex < endIndex && currentLineIndex < lines.Length - 1)
            {
                InterpretLines(currentLineIndex + 1, endIndex);
            }
        }

        private void HandleAssignment(string line)
        {
            string variableName = line.Split('=')[0];

            string variableValue = line.Split('=')[1];
            float finalValue;

            finalValue = HandleMathsExpresison(variableValue);

            //Check if array
            if (squareBracketRegex.Match(variableName).Success)
            {
                string brackets = squareBracketRegex.Match(variableName).Value;
                string arrayName = variableName.Replace(brackets, "");
                int index = (int)HandleMathsExpresison(brackets.Substring(1, brackets.Length - 2));
                UpdateArray(arrayName, index, finalValue);
            }
            else
            {
                //Not an array, therefore assign as normal for variable
                SetVariable(variableName, finalValue);
            }


        }

        private float HandleMathsExpresison(string expression)
        {
            float value = 0;

            /////////////////////////////////////////////////////////
            //Sort out brackets here and do them first recursively

            foreach (Match c in bracketRegex.Matches(expression))
            {
                //This goes and recursively solves brackets
                expression = expression.Replace(c.Value, HandleMathsExpresison(c.Value.Substring(1, c.Value.Length - 2)).ToString());
            }
            if (squareBracketRegex.Match(expression).Success) //If an array reference is found, replace it with the correct array index value
            {
                string brackets = squareBracketRegex.Match(expression).Value;

                int index = (int)HandleMathsExpresison(brackets.Substring(1, brackets.Length - 2));
                expression = expression.Replace(brackets, arrayDelimiter + index.ToString());
            }

            /////////////////////////////////////////////
            //Break down into tokens based on basic operators

            string[] parts = Regex.Split(expression, @"(\+|\*|\-|/|\^)");

            for (int i = 0; i < parts.Length; i++)
            {
                if (variables.ContainsKey(parts[i])) //If a variable is found, replace it with the variables value
                {
                    parts[i] = variables[parts[i]].ToString();
                }
                else if (squareBracketRegex.Match(parts[i]).Success) //If an array reference is found, replace it with the correct array index value
                {
                    string brackets = squareBracketRegex.Match(parts[i]).Value;
                    string arrayName = parts[i].Replace(brackets, "");
                    int index = (int)HandleMathsExpresison(brackets.Substring(1, brackets.Length - 2));
                    parts[i] = GetArrayItem(arrayName, index).ToString();
                }
            }
            //////////////////////////////////////////////////////////////////////////
            //Check for functions and solve them here
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("Sin"))
                {
                    parts[i] = MathF.Sin(float.Parse(parts[i].Substring(3))).ToString();
                }
                else if (parts[i].StartsWith("Asin"))
                {
                    parts[i] = MathF.Asin(float.Parse(parts[i].Substring(4))).ToString();
                }
                else if (parts[i].StartsWith("Sign"))
                {
                    parts[i] = MathF.Sign(float.Parse(parts[i].Substring(4))).ToString();
                }
                else if (parts[i].StartsWith("Sqrt"))
                {
                    parts[i] = MathF.Sqrt(float.Parse(parts[i].Substring(4))).ToString();
                }
                else if (parts[i].StartsWith("Log"))
                {
                    parts[i] = MathF.Log(float.Parse(parts[i].Substring(3))).ToString();
                }

            }

            ////////////////////////////////////////////////////////////////////////
            //Go on to do mathmatical calculationss
            value = EvaluateMathsEquation(parts);

            return value;
        }

        private float EvaluateMathsEquation(string[] equationTokens)
        {
            //by this point should just be operators and numbers
            //need to respect bodmass


            for (int i = 0; i < equationTokens.Length; i++)
            {
                if (equationTokens[i] == "^")
                {

                    equationTokens[i] = MathF.Pow(float.Parse(equationTokens[i - 1]), float.Parse(equationTokens[i + 1])).ToString();
                    equationTokens = RemoveItemsFromArray(equationTokens, new int[] { i - 1, i });
                }
            }
            for (int i = 0; i < equationTokens.Length; i++)
            {
                if (equationTokens[i] == "*")
                {
                    equationTokens[i] = (float.Parse(equationTokens[i - 1]) * float.Parse(equationTokens[i + 1])).ToString();
                    equationTokens = RemoveItemsFromArray(equationTokens, new int[] { i - 1, i });
                }
            }
            for (int i = 0; i < equationTokens.Length; i++)
            {
                if (equationTokens[i] == "/")
                {

                    equationTokens[i] = (float.Parse(equationTokens[i - 1]) / float.Parse(equationTokens[i + 1])).ToString();
                    equationTokens = RemoveItemsFromArray(equationTokens, new int[] { i - 1, i });
                }
            }
            for (int i = 0; i < equationTokens.Length; i++)
            {
                if (equationTokens[i] == "+")
                {
                    equationTokens[i] = (float.Parse(equationTokens[i - 1]) + float.Parse(equationTokens[i + 1])).ToString();
                    equationTokens = RemoveItemsFromArray(equationTokens, new int[] { i - 1, i });
                }
            }
            for (int i = 0; i < equationTokens.Length; i++)
            {
                if (equationTokens[i] == "-")
                {

                    equationTokens[i] = (float.Parse(equationTokens[i - 1]) - float.Parse(equationTokens[i + 1])).ToString();
                    equationTokens = RemoveItemsFromArray(equationTokens, new int[] { i - 1, i });
                }
            }
            return float.Parse(equationTokens[0]);
        }

        private bool HandleConditional(string line)
        {

            //Get condition

            string condition = bracketRegex.Matches(line)[0].Value.Substring(1, bracketRegex.Matches(line)[0].Value.Length - 2);
            condition = condition.Replace(" ", string.Empty);
            //First we need to split for 'or' and 'and' operators
            string[] conditions = Regex.Split(condition, @"(&&|\|\|)");
            for (int i = 0; i < conditions.Length; i = i + 2)
            {
                conditions[i] = Comparison(conditions[i]).ToString();
            }
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i] == "&&")
                {

                    conditions[i] = (bool.Parse(conditions[i - 1]) && bool.Parse(conditions[i + 1])).ToString();
                    conditions = RemoveItemsFromArray(conditions, new int[] { i - 1, i });
                }

            }
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i] == "||")
                {

                    conditions[i] = (bool.Parse(conditions[i - 1]) || bool.Parse(conditions[i + 1])).ToString();
                    conditions = RemoveItemsFromArray(conditions, new int[] { i - 1, i });
                }

            }
            bool statementCondition = bool.Parse(conditions[0]);
            return statementCondition;


        }
        private Boolean Comparison(string condition)
        {
            string[] components = Regex.Split(condition, @"(<|>|==|!=)");
            string LHS = components[0];
            string RHS = components[2];
            string op = components[1];

            float LHSVal = HandleMathsExpresison(LHS);
            float RHSVal = HandleMathsExpresison(RHS);

            if (op == "<")
            {
                return LHSVal < RHSVal;
            }
            else if (op == ">")
            {
                return LHSVal > RHSVal;
            }
            else if (op == "==")
            {
                return LHSVal == RHSVal;
            }
            else if (op == "!=")
            {
                return LHSVal != RHSVal;
            }
            else
            {
                return false;
            }
        }

        private int FindEndIndex(int currentIndex)
        {
            int numOpenFunction = 0;
            int numCloseFunction = 0;
            int newIndex = currentIndex;

            for (int i = 0; i < lines.Length - 1; i++)
            {
                if (numOpenFunction <= numCloseFunction && numOpenFunction > 0)
                {
                    newIndex = currentIndex + i - 1;
                    break;
                }
                numOpenFunction += Regex.Matches(lines[currentIndex + i], @"{").Count;
                numCloseFunction += Regex.Matches(lines[currentIndex + i], @"}").Count;
            }
            return newIndex;
        }
        private string[] RemoveItemsFromArray(string[] arr, int[] indexes)
        {
            var list = new List<string>(arr);
            for (int i = 0; i < indexes.Length; i++)
            {
                list.RemoveAt(indexes[i]);
            }
            return list.ToArray();
        }

        private void OutputMessage(string message)
        {
            Console.WriteLine(message);
        }
        private void OutputErrorMessage(string message)
        {
            Console.WriteLine(message);
        }
    }
}