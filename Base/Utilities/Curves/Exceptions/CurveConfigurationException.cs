using System;
namespace HomeCompanion.Base.Utilities.Curves.Exceptions
{
    public class CurveConfigurationException : CurveException
    {
        public CurveConfigurationException() : base()
        {
        }

        public CurveConfigurationException(string fmt, params object[] args) : base(string.Format(fmt, args))
        {
        }
    }
}
