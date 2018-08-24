using System.Linq;
using System.Text;

namespace Inedo.Extensions.Docker
{
    internal static class Utils
    {
        public static string EscapeLinuxArg(this string arg)
        {
            if (arg.Length > 0 && arg.All(c => char.IsLetterOrDigit(c) || c == '/' || c == '-' || c == '_' || c == '.'))
            {
                return arg;
            }

            return "'" + arg.Replace("'", "'\\''") + "'";
        }

        public static string EscapeWindowsArg(this string arg)
        {
            // https://msdn.microsoft.com/en-us/library/ms880421

            if (!arg.Any(c => char.IsWhiteSpace(c) || c == '\\' || c == '"'))
            {
                return arg;
            }

            var str = new StringBuilder();
            str.Append('"');
            int slashes = 0;
            foreach (char c in arg)
            {
                if (c == '"')
                {
                    str.Append('\\', slashes);
                    str.Append('\\', '"');
                    slashes = 0;
                }
                else if (c == '\\')
                {
                    str.Append('\\');
                    slashes++;
                }
                else
                {
                    str.Append(c);
                    slashes = 0;
                }
            }
            str.Append('\\', slashes);
            str.Append('"');

            return str.ToString();
        }
    }
}
