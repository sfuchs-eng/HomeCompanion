using System;
namespace HomeCompanion.Base.Utilities.Curves.Exceptions
{
    public class CurveException : ApplicationException
    {
        public CurveException() : base()
        {
        }

        public CurveException(string message) : base(message)
        {
        }

        public CurveException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
