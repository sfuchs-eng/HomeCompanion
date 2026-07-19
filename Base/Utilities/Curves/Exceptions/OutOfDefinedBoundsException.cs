using System;
namespace HomeCompanion.Base.Utilities.Curves.Exceptions
{
    public class OutOfDefinedBoundsException : CurveException
    {
        public OutOfDefinedBoundsException() : base()
        {
        }

        public OutOfDefinedBoundsException(string fmt, params object[] args) : base(string.Format(fmt, args))
        {
        }
    }
}
