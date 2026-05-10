namespace Joker.UnityCli.Editor.Models
{
    public class ExecResult
    {
        public string Type = "exec_result";
        public string Id = "";
        public bool Success;
        public string Result = "";
        public string Output = "";
        public string Error = "";
        public long DurationMs;
    }
}
