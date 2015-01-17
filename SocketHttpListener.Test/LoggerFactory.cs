using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Patterns.Logging;

namespace SocketHttpListener.Test
{
    internal static class LoggerFactory
    {
        internal static Mock<ILogger> CreateLogger()
        {
            Mock<ILogger> logger = new Mock<ILogger>();

            SetupConsoleOutput(logger, x => x.Debug(It.IsAny<string>(), It.IsAny<object[]>()));
            SetupConsoleOutput(logger, x => x.Error(It.IsAny<string>(), It.IsAny<object[]>()));
            SetupConsoleOutput(logger, x => x.Fatal(It.IsAny<string>(), It.IsAny<object[]>()));
            SetupConsoleOutput(logger, x => x.Info(It.IsAny<string>(), It.IsAny<object[]>()));
            SetupConsoleOutput(logger, x => x.Warn(It.IsAny<string>(), It.IsAny<object[]>()));
            SetupConsoleOutputException(logger, x => x.ErrorException(It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<object[]>()));
            SetupConsoleOutputException(logger, x => x.FatalException(It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<object[]>()));

            logger.Object.Debug("TEST");

            return logger;
        }
        private static void SetupConsoleOutput(Mock<ILogger> logger, Expression<Action<ILogger>> action)
        {
            logger.Setup(action).Callback<string, object[]>(Console.WriteLine);
        }

        private static void SetupConsoleOutputException(Mock<ILogger> logger, Expression<Action<ILogger>> action)
        {
            logger.Setup(action).Callback<string, Exception, object[]>((x, y, z) =>
            {
                string result = string.Format(x, z);
                Console.WriteLine("{0} {1}Exception:{2} {1}Stack:{3}", result, Environment.NewLine, y.Message,
                    y.StackTrace);
            });
        }
    }
}
