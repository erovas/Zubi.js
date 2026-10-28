namespace Zubi.js
{
    internal sealed class RpcException : Exception
    {
        public string Code { get; }
        public RpcException(string code, string message) : base(message) 
        {
            Code = code;
        } 
    }
}