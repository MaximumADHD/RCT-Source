using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RobloxClientTracker.Exceptions
{
    internal class RoutineFailedException : Exception
    {
        public RoutineFailedException(string message) : base(message) 
        { 
        }
    }
}
