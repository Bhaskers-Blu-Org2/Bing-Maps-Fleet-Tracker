﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Trackable.TripDetection.Exceptions
{
    public class PipelineDataTypeException : Exception
    {
        public PipelineDataTypeException(string detailedMessage)
            :base(detailedMessage)
        {
        }
    }
}
