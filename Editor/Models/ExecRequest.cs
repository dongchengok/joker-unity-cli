namespace Joker.UnityCli.Editor.Models
{
    public class ExecRequest
    {
        public string Type = "exec";
        public string Id = "";
        public string Code = "";
        public string Mode = "script";
        public int Timeout = 30000;
    }
}
