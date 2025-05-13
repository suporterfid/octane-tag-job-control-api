using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OctaneTagWritingTest
{
    public interface IJobStrategy
    {
        void RunJob(CancellationToken cancellationToken = default);
    }
}
