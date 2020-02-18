using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomLanguage
{
    class Program
    {
        static void Main(string[] args)
        {
            string rawCode = System.IO.File.ReadAllText(@"code.txt");
            Interpreter interp = new Interpreter(rawCode);
        }
    }
}
