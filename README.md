SocketHttpListener
==================

A standalone HttpListener with support for WebSockets and Mono

As part of Media Browser Server we needed an http server implementation that could support both WebSockets and Mono together on a single port.

This code was originally forked from websocket-sharp:

https://github.com/sta/websocket-sharp

websocket-sharp was originally a clone of the mono HttpListener found here:

https://github.com/mono/mono/tree/master/mcs/class/System/System.Net

It also added WebSocket support. Over time websocket-sharp began to introduce a lot of refactoring whereas I prefer a straight clone of the mono implementation with added web socket support. So I rebased from the mono version and added web socket support.

In addition, there are a few very minor differences with the mono HttpListener:

* Added ILogger dependency for application logging
* Resolved an issue parsing http headers from Upnp devices. (We need to submit a pull request to mono for this).
* Worked around a known issue with Socket.AcceptAsync and windows (Also need to submit a pull request). See: https://github.com/MediaBrowser/SocketHttpListener/blob/master/SocketHttpListener/Net/EndPointListener.cs#L170
* I have replaced the BeginGetContext with a simple Action delegate. Unlike the .NET HttpListener this is not hooking into http.sys, therefore the only reason for the internal queue was to match the api. Now the consumer can decide how to handle this.
