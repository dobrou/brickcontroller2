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

namespace BrickController2.PlatformServices.GameController
{  
    public class HttpControllerService : IHttpControllerService, IDisposable
    {
        private IDisposable _listenerSubscription;
        private HttpListener _httpListener;

        public event EventHandler<GameControllerEventArgs> GameControllerEvent;

        public HttpControllerService()
        {
            Start();
        }

        public void Start()
        {

            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add("http://*:8080/");
            _httpListener.Start();

            _listenerSubscription =
                // Listen on http endpoint
                Observable.FromAsync(() => _httpListener.GetContextAsync())
                .Repeat()
                // Fire key events from request
                .ObserveOnUsingNewEventLoopSchedulerOnBackground()                
                .Select(ProcessHttpRequest)
                // Send response to client
                .ObserveOn(TaskPoolScheduler.Default)
                .Do(SendResponse)
                .Retry()
                .Subscribe()
            ;
        }

        public void Stop()
        {
            using (_listenerSubscription) _listenerSubscription = null;
            _httpListener.Stop();
            _lastSeenKeyOrder.Clear();
        }

        public void Dispose()
        {
            Stop();
        }

        private readonly Dictionary<(GameControllerEventType type, string key), long> _lastSeenKeyOrder = new Dictionary<(GameControllerEventType type, string key), long>();

        (HttpListenerContext request, HttpStatusCode code, string message) ProcessHttpRequest(HttpListenerContext request)
        {
            try
            {
                // Example: http://phoneip:8080/Button/X/1
                // Example: http://phoneip:8080/Axis/X/-0.5                
                var tokens = request?.Request?.Url?.AbsolutePath?.Trim('/').Split('/') ?? new string[0];

                var tokensPerEvent = 4;

                if (tokens.Length < tokensPerEvent)
                {
                    // unknown url pattern, show main page
                    return (request, HttpStatusCode.OK, defaultWebPage);
                }

                var type = Enum.TryParse<GameControllerEventType>(tokens[0], out var _type) ? _type : GameControllerEventType.Button;
                var key = tokens[1];
                var value = float.TryParse(tokens[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var _value) ? _value : 0;
                var order = long.TryParse(tokens[3], out var _order) ? _order : long.MinValue;
                // make sure to skip out of order key presses, so that we won't override eg. last button release event
                var isSkipped =
                    order != long.MinValue
                    && _lastSeenKeyOrder.TryGetValue((type, key), out var lastOrder)
                    && order < lastOrder
                ;
            
                // write current order to cache
                if(!isSkipped && order != long.MinValue)
                    _lastSeenKeyOrder[(type, key)] = order;


                if (!isSkipped)
                    GameControllerEvent?.Invoke(this, new GameControllerEventArgs(type, key, value));

                return (request, HttpStatusCode.OK, $"{type}:{key}:{value}:{isSkipped}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return (request, HttpStatusCode.InternalServerError, ex.ToString());
            }
        }

        void SendResponse((HttpListenerContext request, HttpStatusCode code, string message) request)
        {
            try
            {
                request.request.Response.StatusCode = (int) request.code;
                request.request.Response.StatusDescription = request.code.ToString();
                request.request.Response.ContentType = "text/html; charset=UTF-8";
                var buffer = Encoding.UTF8.GetBytes(request.message);
                request.request.Response.Close(buffer, false);
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
    var time = value == 0 ? 0 : 50;
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
