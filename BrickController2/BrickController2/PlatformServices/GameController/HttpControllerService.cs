using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BrickController2.Helpers;
using ISimpleHttpListener.Rx.Model;
using SimpleHttpListener.Rx.Extension;
using SimpleHttpListener.Rx.Model;
using SimpleHttpListener.Rx.Service;

namespace BrickController2.PlatformServices.GameController
{
    public class HttpControllerService : IHttpControllerService, IDisposable
    {
        private CancellationTokenSource _serverCancellationToken;
        private IDisposable _serverListener;

        public event EventHandler<GameControllerEventArgs> GameControllerEvent;

        public HttpControllerService()
        {
            Start();
        }

        public void Start()
        {
            if (_serverListener != null)
                return;

            var tcpListener = new TcpListener(IPAddress.Any, 8080)
            {
                ExclusiveAddressUse = false,                
            };

            var httpSender = new HttpSender();

            _serverCancellationToken = new CancellationTokenSource();

            _serverListener =
                tcpListener
                .ToHttpListenerObservable(_serverCancellationToken.Token)
                // Fire key events from request
                .ObserveOnUsingNewEventLoopSchedulerOnBackground()
                .TimeInterval()
                .Select(x => ProcessHttpRequest(x.Value, x.Interval))
                // Send response to client
                .ObserveOn(TaskPoolScheduler.Default)
                .Select(r => Observable.FromAsync(() => SendResponse(r.request, httpSender, r.code, r.message)))
                .Concat()
                .Subscribe()
            ;
        }

        public void Stop()
        {
            using (_serverListener) _serverListener = null;
            using (_serverCancellationToken) _serverCancellationToken = null;
            _lastSeenKeyOrder.Clear();
        }

        public void Dispose()
        {
            Stop();
        }

        private readonly Dictionary<(GameControllerEventType type, string key), long> _lastSeenKeyOrder = new Dictionary<(GameControllerEventType type, string key), long>();

        (IHttpRequestResponse request, HttpStatusCode code, string message) ProcessHttpRequest(
            IHttpRequestResponse request, TimeSpan interval)
        {
            try
            {
                // Example: http://phoneip:8080/Button/X/1
                // Example: http://phoneip:8080/Axis/X/-0.5
                var tokens = request.Path?.Trim('/').Split('/') ?? new string[0];

                var tokensPerEvent = 4;

                if (tokens.Length < tokensPerEvent)
                {
                    // unknown url pattern, show main page
                    return (request, HttpStatusCode.OK, defaultWebPage);
                }

                var events =
                    Enumerable.Range(0, tokens.Length / tokensPerEvent)
                    // split multiple events
                    .Select(i => Enumerable.Range(0, tokensPerEvent).Select(ii => tokens[i*tokensPerEvent+ii]).ToList() )
                    // parse pressed keys
                    .Select(eventTokens => new {
                        type = Enum.TryParse<GameControllerEventType>(eventTokens[0], out var controllerType) ? controllerType : GameControllerEventType.Button,
                        key = eventTokens[1],
                        value = float.TryParse(eventTokens[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0,
                        order = long.TryParse( eventTokens[3], out var order) ? order : long.MinValue,
                    })    
                    .Select(x => new {
                        x.type,
                        x.key,
                        x.value,
                        x.order,
                        // make sure to skip out of order key presses, so that we won't override eg. last button release event
                        isSkipped = 
                            x.order != long.MinValue 
                            && _lastSeenKeyOrder.TryGetValue((x.type, x.key), out var lastOrder) 
                            && x.order < lastOrder,
                    })
                    .ToList()
                ;

                // write current order values to cache
                events.ForEach(e => {
                    if(!e.isSkipped && e.order != long.MinValue)
                        _lastSeenKeyOrder[(e.type, e.key)] = e.order;
                });

                var eventArgs = events
                        .Where(x => !x.isSkipped)
                        .ToLookup(k => (type: k.type, key: k.key), v => v.value)
                        .ToDictionary(k => k.Key, v => v.Last())
                    ;

                if (eventArgs.Any())
                    GameControllerEvent?.Invoke(this, new GameControllerEventArgs(eventArgs));

                return (request, HttpStatusCode.OK, $"Ms: {interval}\n" + string.Join("\n", events.Select(e => $"{e.type}:{e.key}:{e.value}:{e.isSkipped}") ) );
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return (request, HttpStatusCode.InternalServerError, ex.ToString());
            }
        }

        async Task SendResponse(IHttpRequestResponse request, HttpSender httpSender, HttpStatusCode code, string message)
        {
            try
            {
                var response = new HttpResponse
                {
                    StatusCode = (int) code,
                    ResponseReason = code.ToString(),
                    Headers = new Dictionary<string, string>
                    {
                        {"Date", DateTime.UtcNow.ToString("r")},
                        {"Content-Type", "text/html; charset=UTF-8"},
                    },
                    Body = new MemoryStream(Encoding.UTF8.GetBytes(message))
                };

                await httpSender.SendTcpResponseAsync(request, response).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private string defaultWebPage = @"
<html>
<head>
<title>Brick Controller</title>
<style>
div#controls input[type=range] {
  width: 1pc;
  height: 10pc;
  margin: 2pc;
  -webkit-appearance: slider-vertical;  
}
</style>
</head>
<body>
<div id='controls'>
  <input type='range' min='-1' max='1' step='0.1' id='X' onInput='sendJoyEvent(this);' onTouchEnd='resetJoy(this);' onMouseUp='resetJoy(this);' />
  <input type='range' min='-1' max='1' step='0.1' id='Y' onInput='sendJoyEvent(this);' onTouchEnd='resetJoy(this);' onMouseUp='resetJoy(this);' />
  <input type='range' min='-1' max='1' step='0.1' id='Z' onInput='sendJoyEvent(this);' onTouchEnd='resetJoy(this);' onMouseUp='resetJoy(this);' />
</div>
<div>
  <input type='checkbox' id='joyautoreset' checked />
  <label for='joyautoreset'>Joystick position auto reset</label>
</div>
<div>
  Last key pressed: <div id='lastkey'/>
</div>
<div>
  Last key response: <div id='lastresponse'/>
</div>
<div>
  Press any key to send it as button event.
</div>

<script type='text/javascript'>
  document.addEventListener('keydown', function(event) {
    if(event.repeat) return;  
    sendButtonEvent(event.key, 1);
  });
  document.addEventListener('keyup', function(event) {
    sendButtonEvent(event.key, 0);
  });

  function sendButtonEvent(key, value) {
    if(key.length == 1) key = key.toUpperCase();
    sendEvent('Button', key, value);
  }
    
  function sendEvent(type, key, value) {    
    var url='/'+type+'/'+key+'/'+value+'/'+(new Date().getTime());
  
    document.getElementById('lastkey').textContent = url;
    console.log(url);

    var Http = new XMLHttpRequest();
    Http.open('GET', url);
    Http.send();
    Http.onload = (e)=>{ 
      document.getElementById('lastresponse').textContent = Http.responseText;
      console.log(Http.responseText) 
    };
  }


  function resetJoy(range) {
    if( document.getElementById('joyautoreset').checked ) 
      range.value = 0;
      
    sendJoyEvent(range);
  }
  
  function sendJoyEvent(range) {
    sendEventThrottled('Axis', range.id, range.value );
  }
    
  function sendEventThrottled(type, key, value) { 
    var time = value == 0 ? 0 : 200;
    throttle( type+'.'+key, time, _ => sendEvent(type, key, value) );
  }

  var throttleNextRun = {};
  function throttle(id, time, func) {
    if(time == 0){
      func();
      if(throttleNextRun[id]) throttleNextRun[id] = null;
    } else if(throttleNextRun[id] === undefined){
      func();
      throttleNextRun[id] = null;
      setTimeout(function() { 
        var f = throttleNextRun[id]; 
        throttleNextRun[id] = undefined; 
        if(f) throttle(id, time, f);
      }, time);
    }else{
      throttleNextRun[id] = func;
    }
  };
</script>
</body>
</html>
";

    }
}
