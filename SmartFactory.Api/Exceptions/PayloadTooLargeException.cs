namespace SmartFactory.Api.Exceptions;

public class PayloadTooLargeException : Exception
{
    public PayloadTooLargeException(string message) : base(message)
    {
    }
}
