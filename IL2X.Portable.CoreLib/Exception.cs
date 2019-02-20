namespace System
{
	public class Exception
	{
		public Exception()
		{
		}

		public Exception(string message)
		{
		}
		
		public Exception InnerException
		{
			get
			{
				return null;
			}
		}

		public virtual string Message
		{
			get
			{
				return null;
			}
		}
		
		public virtual string StackTrace
		{
			get
			{
				return null;
			}
		}
	}
}
