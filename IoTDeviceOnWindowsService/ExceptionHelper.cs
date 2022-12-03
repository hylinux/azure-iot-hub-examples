using System.Net;
using System.Net.Sockets;
using DotNetty.Transport.Channels;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;


namespace IoTDeviceOnWindowsService;

internal class ExceptionHelper
{
    private static readonly HashSet<Type> s_networkExceptions = new()
    {
        typeof(IOException),
        typeof(SocketException),
        typeof(ClosedChannelException),
        typeof(TimeoutException),
        typeof(OperationCanceledException),
        typeof(HttpRequestException),
        typeof(WebException),
        typeof(WebSocketException),
    };

    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    internal static bool IsNetworkExceptionChain(Exception chain)
    {
        return Unwind(chain, true).Any( e => IsNetwork(e) && !IsSecurity(e));
    }

    private static bool IsNetwork(Exception singleException)
    {
        return s_networkExceptions.Any( baseException => baseException.IsInstanceOfType(singleException));
    }

    private static bool IsSecurity(Exception singleException)
    {
        if ( singleException is AuthenticationException )
        {
            return true;
        }

        if ( s_isWindows )
        {
            if ( singleException.HResult == unchecked((int)0x80072F8F))
            {
                return true;
            }
        }
        else
        {
            if ( singleException.HResult == 60 )
            {
                return true;
            }
        }

        return false;
    }


    private static IEnumerable<Exception> Unwind(Exception? exception, bool unwindAggregate = false)
    {
        while ( exception != null )
        {
            yield return exception;

            if ( !unwindAggregate )
            {
                exception = exception.InnerException;
                continue;
            }

            if ( exception is AggregateException aggEx && aggEx.InnerException != null )
            {
                foreach (Exception ex in aggEx.InnerExceptions )
                {
                    foreach ( Exception innerEx in Unwind(ex, true) )
                    {
                        yield return innerEx;
                    }
                }
            }

            exception = exception.InnerException;
        }
    }
}
