//+------------------------------------------------------------------+
//| Fleet.mq4 — Fleet engine connector for MetaTrader 4              |
//|                                                                  |
//| Rides FleetBridge.dll — a single, dependency-free native DLL   |
//| built from C# (shared/FleetBridge, NativeAOT win-x86, 32-bit) —  |
//| which owns the FIX session with the engine. Same contract as    |
//| the MT5 connector; MT5-only features (stop-limit orders, real    |
//| volume) are rejected with ERROR_CODE_UNSUPPORTED.                |
//+------------------------------------------------------------------+
#property copyright "UltimateSoftwares"
#property version   "1.10"
#property strict

// Trade-server error codes normally provided by <stderror.mqh>. Inlined with
// guards (identical values to the stock header) because packaged/portable
// MT4 trees may not have unpacked Include\ yet when the EA is compiled.
#ifndef ERR_NO_RESULT
#define ERR_NO_RESULT              1
#endif
#ifndef ERR_NO_CONNECTION
#define ERR_NO_CONNECTION          6
#endif
#ifndef ERR_INVALID_PRICE
#define ERR_INVALID_PRICE          129
#endif
#ifndef ERR_INVALID_STOPS
#define ERR_INVALID_STOPS          130
#endif
#ifndef ERR_INVALID_TRADE_VOLUME
#define ERR_INVALID_TRADE_VOLUME   131
#endif
#ifndef ERR_MARKET_CLOSED
#define ERR_MARKET_CLOSED          132
#endif
#ifndef ERR_TRADE_DISABLED
#define ERR_TRADE_DISABLED         133
#endif
#ifndef ERR_NOT_ENOUGH_MONEY
#define ERR_NOT_ENOUGH_MONEY       134
#endif
#ifndef ERR_PRICE_CHANGED
#define ERR_PRICE_CHANGED          135
#endif
#ifndef ERR_OFF_QUOTES
#define ERR_OFF_QUOTES             136
#endif
#ifndef ERR_BROKER_BUSY
#define ERR_BROKER_BUSY            137
#endif
#ifndef ERR_REQUOTE
#define ERR_REQUOTE                138
#endif
#ifndef ERR_TRADE_TIMEOUT
#define ERR_TRADE_TIMEOUT          128
#endif
#ifndef ERR_INVALID_TICKET
#define ERR_INVALID_TICKET         4108
#endif
#ifndef ERR_TRADE_CONTEXT_BUSY
#define ERR_TRADE_CONTEXT_BUSY     146
#endif
#ifndef ERR_TOO_FREQUENT_REQUESTS
#define ERR_TOO_FREQUENT_REQUESTS  8
#endif

input string InpEngineAddress = "127.0.0.1:50051"; // Engine host:port (fallback when no address file)
input bool   InpUseTls      = false;             // TLS to the engine
input int    InpTimerMs     = 100;               // Pump interval, ms

//--- bridge imports (identical ABI to the MT5 connector) ------------
#import "FleetBridge.dll"
int    Fleet_Version();
int    Fleet_Init(string engine,string terminalId,int platform,
                    string build,long login,string server,int tzMinutes,int useTls,
                    int marginMode,string brokerName);
int    Fleet_Shutdown();
int    Fleet_State();
int    Fleet_LastError(ushort &buf[],int cap);
int    Fleet_HeartbeatIntervalMs();
int    Fleet_NextLog(ushort &buf[],int cap);
int    Fleet_PushTick(string symbol,double bid,double ask,double last,long volume,long timeMs,
                        int spreadPoints,double balance,double equity,double margin,double marginFree);
int    Fleet_BeginHeartbeat(int connected,double equity,double balance,int pendingOrders);
int    Fleet_EndHeartbeat();
int    Fleet_PushPositionEvent(int kind,long ticket,string symbol,int type,double volume,
                                 double priceOpen,double priceCurrent,double sl,double tp,
                                 double profit,double swap,double commission,string comment,
                                 long magic,long openTimeMs);
int    Fleet_PushOrderEvent(int kind,long ticket,string symbol,int type,double volInitial,
                              double volCurrent,double price,double priceCurrent,
                              double stopLimit,double sl,double tp,string comment,
                              long magic,long setupMs,long expirationMs);
int    Fleet_Log(int level,string msg);
int    Fleet_PollCommand(ushort &idBuf[],int cap);
long   Fleet_CmdInt(string name);
double Fleet_CmdDouble(string name);
int    Fleet_CmdString(string name,ushort &buf[],int cap);
int    Fleet_SendAck(string cmdId);
int    Fleet_SendError(string cmdId,int code,int platformCode,string msg,int retryable);
int    Fleet_SendOrderResult(string cmdId,long orderTicket,long positionTicket,
                               double price,double volume,string clientOrderId);
int    Fleet_SendAccountInfo(string cmdId,long login,string server,string company,string currency,
                               double balance,double equity,double margin,double marginFree,
                               double marginLevel,int leverage,int tradeAllowed,string name,
                               int tradeMode);
int    Fleet_BeginResult(string cmdId);
int    Fleet_AddBar(long timeMs,double open,double high,double low,double close,
                      long tickVolume,long realVolume,int spread);
int    Fleet_AddPosition(long ticket,string symbol,int type,double volume,double priceOpen,
                           double priceCurrent,double sl,double tp,double profit,double swap,
                           double commission,string comment,long magic,long openTimeMs);
int    Fleet_AddOrder(long ticket,string symbol,int type,double volInitial,double volCurrent,
                        double price,double priceCurrent,double stopLimit,double sl,double tp,
                        string comment,long magic,long setupMs,long expirationMs);
int    Fleet_AddSymbolInfo(string symbol,string description,int digits,double point,
                             double tickSize,double tickValue,double contractSize,
                             double volMin,double volMax,double volStep,
                             int stopsLevel,int freezeLevel,double marginRequired,
                             string curBase,string curProfit,string curMargin,
                             int tradeAllowed,long spreadPoints);
int    Fleet_AddDeal(long ticket,long orderTicket,long positionTicket,string symbol,int type,
                       double volume,double price,double profit,double swap,double commission,
                       string comment,long magic,long timeMs);
int    Fleet_AddDealV2(long ticket,long orderTicket,long positionTicket,string symbol,int type,
                       double volume,double price,double profit,double swap,double commission,
                       string comment,long magic,long timeMs,int entry);
int    Fleet_AddDealV3(long ticket,long orderTicket,long positionTicket,string symbol,int type,
                       double volume,double price,double profit,double swap,double commission,
                       string comment,long magic,long timeMs,int entry,double fee);
int    Fleet_AddCloseResult(long ticket,int ok,double closedVolume,double closePrice,
                              int errorCode,int platformCode,string message,int retryable);
int    Fleet_EndResult(int payloadType);
#import

//--- protocol constants (keep in sync with FleetBridge/Consts.cs)
#define CMD_SUBSCRIBE          1
#define CMD_UNSUBSCRIBE        2
#define CMD_PLACE_ORDER        3
#define CMD_CLOSE_POSITION     4
#define CMD_GET_ACCOUNT_INFO   5
#define CMD_GET_HISTORY        6
#define CMD_GET_POSITIONS      7
#define CMD_GET_PENDING_ORDERS 8
#define CMD_MODIFY_POSITION    9
#define CMD_CANCEL_ORDER       10
#define CMD_MODIFY_ORDER       11
#define CMD_GET_SYMBOL_INFO    12
#define CMD_GET_DEALS_HISTORY  13
#define CMD_CLOSE_POSITIONS_BATCH 14
#define CMD_SUBSCRIBE_DEPTH    15
#define CMD_UNSUBSCRIBE_DEPTH  16

#define PAYLOAD_HISTORY        1
#define PAYLOAD_POSITIONS      2
#define PAYLOAD_PENDING        3
#define PAYLOAD_SYMBOLS        4
#define PAYLOAD_DEALS          5
#define PAYLOAD_BATCH_CLOSE    6

// mtfleet.v1.ErrorCode (MTF_ prefix avoids clashing with MT4 built-in ERR_*)
#define MTF_ERR_INTERNAL           1
#define MTF_ERR_UNSUPPORTED        2
#define MTF_ERR_INVALID_REQUEST    3
#define MTF_ERR_NOT_FOUND          4
#define MTF_ERR_TRADE_DISABLED     5
#define MTF_ERR_MARKET_CLOSED      6
#define MTF_ERR_NOT_ENOUGH_MONEY   7
#define MTF_ERR_INVALID_PRICE      8
#define MTF_ERR_INVALID_STOPS      9
#define MTF_ERR_INVALID_VOLUME     10
#define MTF_ERR_REQUOTE            11
#define MTF_ERR_PRICE_CHANGED      12
#define MTF_ERR_TIMEOUT            13
#define MTF_ERR_TOO_MANY_REQUESTS  14
#define MTF_ERR_REJECTED           15
#define MTF_ERR_CONNECTION_LOST    16

#define EVT_POSITION_OPENED    1
#define EVT_POSITION_CLOSED    2
#define EVT_POSITION_MODIFIED  3
#define EVT_ORDER_PLACED       4
#define EVT_ORDER_MODIFIED     5
#define EVT_ORDER_CANCELLED    6
#define EVT_ORDER_FILLED       7
#define EVT_ORDER_EXPIRED      8

#define MTF_ABI_VERSION        10  // must match FleetBridge Consts.AbiVersion (Consts.cs)

//--- state ----------------------------------------------------------
string g_terminalId="";      // resolved identity (input override or persisted GUID)
string g_engineAddress="";   // resolved engine endpoint (address file or input)
string g_symbols[];          // engine-subscribed symbols
long   g_lastTickTime[];     // per-symbol last pushed tick (sec)
double g_lastBid[];
double g_lastAsk[];
int    g_knownTickets[];     // open market-order tickets, for trade-event diffing
double g_knownSL[];          // SL per known ticket (parallel to g_knownTickets)
double g_knownTP[];          // TP per known ticket (parallel to g_knownTickets)
int    g_pendTickets[];      // pending-order tickets, for ORDER_* event diffing
int    g_pendType[];         // original pending type (fills flip it to OP_BUY/OP_SELL)
double g_pendPrice[];        // entry price per pending ticket
double g_pendSL[];
double g_pendTP[];
long   g_pendExp[];          // expiration, epoch seconds (0 = GTC)
long   g_lastHeartbeat=0;

//--- terminal identity ----------------------------------------------
// Engine sessions are keyed by terminal_id. fleetd assigns a GUID when the
// admin provisions the terminal and stamps it into the data folder
// (MQL4\Files\mtfleet_terminal_id.txt; terminals run /portable), and the
// EA introduces itself with that id on connect. An unmanaged install
// without the file self-provisions a UUIDv4 once and persists it there.
#define MTF_ID_FILE "mtfleet_terminal_id.txt"

// Engine endpoint resolution: fleetd stamps the engine's TerminalService
// address into MQL4\Files\mtfleet_engine_address.txt (re-stamped on every
// managed start). When the file is absent or empty the InpEngineAddress
// input applies, so unmanaged installs keep working.
#define MTF_ENGINE_FILE "mtfleet_engine_address.txt"

string TrimIdString(string s)
{
   int b=0,e=StringLen(s)-1;
   while(b<=e)
   {
      ushort c=StringGetCharacter(s,b);
      if(c==' '||c=='\t'||c=='\r'||c=='\n') b++; else break;
   }
   while(e>=b)
   {
      ushort c=StringGetCharacter(s,e);
      if(c==' '||c=='\t'||c=='\r'||c=='\n') e--; else break;
   }
   if(e<b) return "";
   return StringSubstr(s,b,e-b+1);
}

string GenerateTerminalGuid()
{
   // UUIDv4 from MathRand bytes; seeded with high-resolution counters so two
   // terminals started in the same second do not collide.
   MathSrand((int)((long)GetMicrosecondCount()^(long)GetTickCount()
                   ^(long)TimeLocal()^AccountInfoInteger(ACCOUNT_LOGIN)));
   uchar b[16];
   ArrayInitialize(b,0);
   for(int i=0;i<16;i++)
      b[i]=(uchar)((MathRand()^(MathRand()<<7))&0xFF);
   b[6]=(uchar)((b[6]&0x0F)|0x40); // version 4
   b[8]=(uchar)((b[8]&0x3F)|0x80); // RFC 4122 variant
   string id="";
   for(int i=0;i<16;i++)
   {
      id+=StringFormat("%02x",b[i]);
      if(i==3||i==5||i==7||i==9) id+="-";
   }
   return id;
}

string ResolveEngineAddress()
{
   int h=FileOpen(MTF_ENGINE_FILE,FILE_READ|FILE_TXT|FILE_ANSI);
   if(h!=INVALID_HANDLE)
   {
      string addr=TrimIdString(FileReadString(h));
      FileClose(h);
      if(StringLen(addr)>0)
      {
         Print("Fleet: engine address '",addr,"' (MQL4\\Files\\",MTF_ENGINE_FILE,")");
         return addr;
      }
   }
   return InpEngineAddress;
}

string ResolveTerminalId()
{
   int h=FileOpen(MTF_ID_FILE,FILE_READ|FILE_TXT|FILE_ANSI);
   if(h!=INVALID_HANDLE)
   {
      string id=TrimIdString(FileReadString(h));
      FileClose(h);
      if(StringLen(id)>0)
      {
         Print("Fleet: terminal id '",id,"' (MQL4\\Files\\",MTF_ID_FILE,")");
         return id;
      }
   }
   string fresh=GenerateTerminalGuid();
   h=FileOpen(MTF_ID_FILE,FILE_WRITE|FILE_TXT|FILE_ANSI);
   if(h!=INVALID_HANDLE)
   {
      FileWriteString(h,fresh);
      FileClose(h);
      Print("Fleet: generated terminal id '",fresh,"' (persisted in MQL4\\Files\\",MTF_ID_FILE,")");
   }
   else
      Print("Fleet: WARNING: generated terminal id '",fresh,
            "' but could not persist it (err ",GetLastError(),
            "); a new id will be generated next start");
   return fresh;
}

//+------------------------------------------------------------------+
//+------------------------------------------------------------------+
// One connector per terminal: the startup chart ([StartUp] in the
// start-config ini) and a restored profile chart can both carry Fleet, and
// a second Fleet_Init would restart the shared bridge. MQL4 cannot read
// another chart's EA name, so the owner chart id lives in a temporary
// (never persisted) terminal global; a live owner wins and a losing startup
// twin (same symbol/period) closes its chart too.
#define FLEET_OWNER_GV "Fleet.ConnectorChart"
bool g_bridgeOwner=false; // only the instance that called Fleet_Init shuts the shared bridge down

bool FleetDuplicate()
{
   long self=ChartID();
   if(GlobalVariableCheck(FLEET_OWNER_GV))
   {
      long owner=(long)GlobalVariableGet(FLEET_OWNER_GV);
      if(owner!=self && ChartSymbol(owner)!="")
      {
         Print("Fleet: connector already running on chart ",owner," (",ChartSymbol(owner),"); not starting a second one");
         if(ChartSymbol(owner)==Symbol() && ChartPeriod(owner)==Period())
            ChartClose(self);
         return true;
      }
   }
   GlobalVariableTemp(FLEET_OWNER_GV);
   GlobalVariableSet(FLEET_OWNER_GV,(double)self);
   return false;
}

int OnInit()
{
   if(FleetDuplicate())
      return INIT_FAILED;
   if(!TerminalInfoInteger(TERMINAL_DLLS_ALLOWED))
   {
      Print("Fleet: enable 'Allow DLL imports' in Tools > Options > Expert Advisors");
      return INIT_FAILED;
   }
   g_terminalId=ResolveTerminalId();
   if(g_terminalId=="")
   {
      Print("Fleet: could not resolve a terminal id");
      return INIT_FAILED;
   }
   int ver=Fleet_Version();
   if(ver!=MTF_ABI_VERSION)
   {
      Print("Fleet: FleetBridge.dll ABI version ",ver," does not match EA (",MTF_ABI_VERSION,"); redeploy matching builds");
      return INIT_FAILED;
   }
   int tz=(int)((TimeCurrent()-TimeGMT())/60);
   g_engineAddress=ResolveEngineAddress();
   g_bridgeOwner=true;
   int rc=Fleet_Init(g_engineAddress,g_terminalId,1 /*PLATFORM_MT4*/,
                       IntegerToString(TerminalInfoInteger(TERMINAL_BUILD)),
                       AccountInfoInteger(ACCOUNT_LOGIN),
                       AccountInfoString(ACCOUNT_SERVER),tz,InpUseTls?1:0,
                       2 /*MARGIN_MODE_HEDGING: MT4 always hedges*/,
                       AccountInfoString(ACCOUNT_COMPANY));
   if(rc!=0)
   {
      Print("Fleet: bridge init failed (rc=",rc,"): ",BridgeLastError());
      Print("Fleet: verify MQL4\\Libraries contains the 32-bit FleetBridge.dll ",
            "(cargo build --release -p fleet-bridge --target i686-pc-windows-gnu)");
      return INIT_FAILED;
   }
   SnapshotTickets(g_knownTickets,g_knownSL,g_knownTP); // baseline, no events for pre-existing trades
   SnapshotPendings(g_pendTickets,g_pendType,g_pendPrice,g_pendSL,g_pendTP,g_pendExp);
   EventSetMillisecondTimer(MathMax(InpTimerMs,20));
   Print("Fleet: connector started, engine=",g_engineAddress,", terminal_id=",g_terminalId);
   PumpBridgeLogs();
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
   EventKillTimer();
   if(g_bridgeOwner)
      Fleet_Shutdown();
   g_bridgeOwner=false;
   if(GlobalVariableCheck(FLEET_OWNER_GV) && (long)GlobalVariableGet(FLEET_OWNER_GV)==ChartID())
      GlobalVariableDel(FLEET_OWNER_GV);
}

void OnTick()
{
   PushSymbolTick(Symbol());
}

void OnTimer()
{
   PumpBridgeLogs();
   for(int i=0;i<ArraySize(g_symbols);i++)
      PushSymbolTick(g_symbols[i]);
   PumpCommands();
   DetectTradeEvents();
   MaybeHeartbeat();
}

//+------------------------------------------------------------------+
//| Drain bridge event log (connects, backoff, session state, ...)   |
//| into the Experts tab. Bounded per call to keep the timer snappy. |
//+------------------------------------------------------------------+
void PumpBridgeLogs()
{
   ushort buf[512];
   for(int i=0;i<20;i++)
   {
      int n=Fleet_NextLog(buf,512);
      if(n<=0) break;
      Print("Fleet: ",ShortArrayToString(buf,0,n));
   }
}

//+------------------------------------------------------------------+
//| Pumps                                                            |
//+------------------------------------------------------------------+
void PushSymbolTick(const string symbol)
{
   int idx=SymbolIndex(symbol);
   if(idx<0) return;
   MqlTick t;
   if(!SymbolInfoTick(symbol,t)) return;
   // MT4 ticks have second resolution: dedupe on (time, bid, ask).
   if((long)t.time==g_lastTickTime[idx] && t.bid==g_lastBid[idx] && t.ask==g_lastAsk[idx]) return;
   g_lastTickTime[idx]=(long)t.time;
   g_lastBid[idx]=t.bid;
   g_lastAsk[idx]=t.ask;
   int spread=(int)MarketInfo(symbol,MODE_SPREAD); // points
   Fleet_PushTick(symbol,t.bid,t.ask,t.last,(long)t.volume,(long)t.time*1000,spread,
                    AccountBalance(),AccountEquity(),AccountMargin(),AccountFreeMargin());
}

void MaybeHeartbeat()
{
   long iv=Fleet_HeartbeatIntervalMs();
   if(iv<=0) iv=15000;
   long now=(long)TimeGMT()*1000;
   if(now-g_lastHeartbeat < iv/2) return;
   g_lastHeartbeat=now;
   int pending=0;
   for(int i=0;i<OrdersTotal();i++)
   {
      if(!OrderSelect(i,SELECT_BY_POS)) continue;
      if(OrderType()>OP_SELL) pending++;
   }
   Fleet_BeginHeartbeat(IsConnected()?1:0,AccountEquity(),AccountBalance(),pending);
   // Full open-position snapshot (floating PnL included) rides in every
   // heartbeat so the engine mirrors exposure without polling get_positions.
   for(int i=0;i<OrdersTotal();i++)
   {
      if(!OrderSelect(i,SELECT_BY_POS)) continue;
      if(OrderType()>OP_SELL) continue;
      double priceCur=(OrderType()==OP_BUY?MarketInfo(OrderSymbol(),MODE_BID)
                                          :MarketInfo(OrderSymbol(),MODE_ASK));
      Fleet_AddPosition(OrderTicket(),OrderSymbol(),OrderType()+1,OrderLots(),
                          OrderOpenPrice(),priceCur,OrderStopLoss(),OrderTakeProfit(),
                          OrderProfit(),OrderSwap(),OrderCommission(),OrderComment(),
                          OrderMagicNumber(),(long)OrderOpenTime()*1000);
   }
   Fleet_EndHeartbeat();
}

void PumpCommands()
{
   ushort idBuf[64];
   for(int i=0;i<32;i++)
   {
      int type=Fleet_PollCommand(idBuf,64);
      if(type==0) break;
      string id=ShortArrayToString(idBuf);
      Dispatch(type,id);
   }
}

void Dispatch(const int type,const string id)
{
   switch(type)
   {
      case CMD_SUBSCRIBE:          HandleSubscribe(id,true);   break;
      case CMD_UNSUBSCRIBE:        HandleSubscribe(id,false);  break;
      case CMD_PLACE_ORDER:        HandlePlaceOrder(id);       break;
      case CMD_CLOSE_POSITION:     HandleClosePosition(id);    break;
      case CMD_GET_ACCOUNT_INFO:   HandleGetAccountInfo(id);   break;
      case CMD_GET_HISTORY:        HandleGetHistory(id);       break;
      case CMD_GET_POSITIONS:      HandleGetPositions(id);     break;
      case CMD_GET_PENDING_ORDERS: HandleGetPendingOrders(id); break;
      case CMD_MODIFY_POSITION:    HandleModifyPosition(id);   break;
      case CMD_CANCEL_ORDER:       HandleCancelOrder(id);      break;
      case CMD_MODIFY_ORDER:       HandleModifyOrder(id);      break;
      case CMD_GET_SYMBOL_INFO:    HandleGetSymbolInfo(id);    break;
      case CMD_GET_DEALS_HISTORY:  HandleGetDeals(id);         break;
      case CMD_CLOSE_POSITIONS_BATCH: HandleCloseBatch(id);    break;
      case CMD_SUBSCRIBE_DEPTH:
      case CMD_UNSUBSCRIBE_DEPTH:
         // MQL4 has no market-depth API (no MarketBook* functions).
         Fleet_SendError(id,MTF_ERR_UNSUPPORTED,0,"market depth is not available on MT4",0);
         break;
      default:
         Fleet_SendError(id,MTF_ERR_UNSUPPORTED,0,"unknown command type",0);
   }
}

//+------------------------------------------------------------------+
//| Trade event detection (MT4 has no OnTradeTransaction)            |
//+------------------------------------------------------------------+
void SnapshotTickets(int &tickets[],double &sl[],double &tp[])
{
   ArrayResize(tickets,0);
   ArrayResize(sl,0);
   ArrayResize(tp,0);
   for(int i=0;i<OrdersTotal();i++)
   {
      if(!OrderSelect(i,SELECT_BY_POS)) continue;
      if(OrderType()>OP_SELL) continue;
      int n=ArraySize(tickets);
      ArrayResize(tickets,n+1);
      ArrayResize(sl,n+1);
      ArrayResize(tp,n+1);
      tickets[n]=OrderTicket();
      sl[n]=OrderStopLoss();
      tp[n]=OrderTakeProfit();
   }
}

// Snapshot of live pending orders (OP_BUYLIMIT..OP_SELLSTOP) with the
// parameters a modify can change.
void SnapshotPendings(int &tickets[],int &type[],double &price[],double &sl[],double &tp[],long &exp[])
{
   ArrayResize(tickets,0);
   ArrayResize(type,0);
   ArrayResize(price,0);
   ArrayResize(sl,0);
   ArrayResize(tp,0);
   ArrayResize(exp,0);
   for(int i=0;i<OrdersTotal();i++)
   {
      if(!OrderSelect(i,SELECT_BY_POS)) continue;
      if(OrderType()<=OP_SELL) continue;
      int n=ArraySize(tickets);
      ArrayResize(tickets,n+1);
      ArrayResize(type,n+1);
      ArrayResize(price,n+1);
      ArrayResize(sl,n+1);
      ArrayResize(tp,n+1);
      ArrayResize(exp,n+1);
      tickets[n]=OrderTicket();
      type[n]=OrderType();
      price[n]=OrderOpenPrice();
      sl[n]=OrderStopLoss();
      tp[n]=OrderTakeProfit();
      exp[n]=(long)OrderExpiration();
   }
}

int IndexOf(const int &arr[],const int v)
{
   for(int i=0;i<ArraySize(arr);i++)
      if(arr[i]==v) return i;
   return -1;
}

bool InArray(const int &arr[],const int v)
{
   return IndexOf(arr,v)>=0;
}

void DetectTradeEvents()
{
   int    current[];
   double curSL[],curTP[];
   SnapshotTickets(current,curSL,curTP);

   for(int i=0;i<ArraySize(current);i++)
   {
      int prev=IndexOf(g_knownTickets,current[i]);
      if(prev<0)
      {
         // Newly opened position.
         if(OrderSelect(current[i],SELECT_BY_TICKET))
            PushOrderAsPositionEvent(EVT_POSITION_OPENED);
      }
      else if(curSL[i]!=g_knownSL[prev] || curTP[i]!=g_knownTP[prev])
      {
         // SL/TP changed (manually or by another EA/script).
         if(OrderSelect(current[i],SELECT_BY_TICKET))
            PushOrderAsPositionEvent(EVT_POSITION_MODIFIED);
      }
   }

   // Closed positions (OrderSelect by ticket also searches history).
   for(int j=0;j<ArraySize(g_knownTickets);j++)
      if(!InArray(current,g_knownTickets[j]) && OrderSelect(g_knownTickets[j],SELECT_BY_TICKET))
         PushOrderAsPositionEvent(EVT_POSITION_CLOSED);

   int n=ArraySize(current);
   ArrayResize(g_knownTickets,n);
   ArrayResize(g_knownSL,n);
   ArrayResize(g_knownTP,n);
   for(int k=0;k<n;k++)
   {
      g_knownTickets[k]=current[k];
      g_knownSL[k]=curSL[k];
      g_knownTP[k]=curTP[k];
   }

   // Pending orders: ORDER_PLACED for new tickets, ORDER_MODIFIED when
   // price/SL/TP/expiration of a tracked ticket changed, and ORDER_FILLED/
   // ORDER_CANCELLED/ORDER_EXPIRED when a ticket vanishes.
   int    pend[],pendType[];
   double pendPrice[],pendSL[],pendTP[];
   long   pendExp[];
   SnapshotPendings(pend,pendType,pendPrice,pendSL,pendTP,pendExp);

   for(int p=0;p<ArraySize(pend);p++)
   {
      int prev=IndexOf(g_pendTickets,pend[p]);
      if(prev<0)
      {
         if(OrderSelect(pend[p],SELECT_BY_TICKET))
            PushOrderAsOrderEvent(EVT_ORDER_PLACED);
         continue;
      }
      if(pendPrice[p]!=g_pendPrice[prev] || pendSL[p]!=g_pendSL[prev] ||
         pendTP[p]!=g_pendTP[prev] || pendExp[p]!=g_pendExp[prev])
      {
         if(OrderSelect(pend[p],SELECT_BY_TICKET))
            PushOrderAsOrderEvent(EVT_ORDER_MODIFIED);
      }
   }

   // Vanished pendings: a filled ticket's type flips to OP_BUY/OP_SELL (even
   // in history) -> ORDER_FILLED, reported with the original pending type
   // (the fill itself also surfaces as POSITION_OPENED above). A still-pending
   // type in history means the order was deleted or expired.
   for(int d=0;d<ArraySize(g_pendTickets);d++)
   {
      if(InArray(pend,g_pendTickets[d])) continue;
      if(!OrderSelect(g_pendTickets[d],SELECT_BY_TICKET)) continue;
      if(OrderType()<=OP_SELL)
      {
         PushOrderAsOrderEvent(EVT_ORDER_FILLED,g_pendType[d]+1);
         continue;
      }
      bool expired=(OrderExpiration()>0 && OrderCloseTime()>=OrderExpiration()) ||
                   StringFind(OrderComment(),"expir")>=0;
      PushOrderAsOrderEvent(expired?EVT_ORDER_EXPIRED:EVT_ORDER_CANCELLED);
   }

   int np=ArraySize(pend);
   ArrayResize(g_pendTickets,np);
   ArrayResize(g_pendType,np);
   ArrayResize(g_pendPrice,np);
   ArrayResize(g_pendSL,np);
   ArrayResize(g_pendTP,np);
   ArrayResize(g_pendExp,np);
   for(int q=0;q<np;q++)
   {
      g_pendTickets[q]=pend[q];
      g_pendType[q]=pendType[q];
      g_pendPrice[q]=pendPrice[q];
      g_pendSL[q]=pendSL[q];
      g_pendTP[q]=pendTP[q];
      g_pendExp[q]=pendExp[q];
   }
}

// Emits a position event from the currently selected order.
void PushOrderAsPositionEvent(const int kind)
{
   double priceCur=(kind==EVT_POSITION_CLOSED?OrderClosePrice():
                    (OrderType()==OP_BUY?MarketInfo(OrderSymbol(),MODE_BID)
                                        :MarketInfo(OrderSymbol(),MODE_ASK)));
   Fleet_PushPositionEvent(kind,OrderTicket(),OrderSymbol(),OrderType()+1,OrderLots(),
                             OrderOpenPrice(),priceCur,OrderStopLoss(),OrderTakeProfit(),
                             OrderProfit(),OrderSwap(),OrderCommission(),OrderComment(),
                             OrderMagicNumber(),(long)OrderOpenTime()*1000);
}

// Emits a pending-order event from the currently selected order. For fills
// the selected (historical) order already shows the market type, so callers
// pass the original pending type via typeOverride (proto value, i.e. +1).
void PushOrderAsOrderEvent(const int kind,const int typeOverride=0)
{
   double priceCur=(OrderType()==OP_BUYLIMIT||OrderType()==OP_BUYSTOP
                    ? MarketInfo(OrderSymbol(),MODE_ASK)
                    : MarketInfo(OrderSymbol(),MODE_BID));
   int ptype=(typeOverride>0?typeOverride:OrderType()+1);
   Fleet_PushOrderEvent(kind,OrderTicket(),OrderSymbol(),ptype,
                          OrderLots(),OrderLots(),OrderOpenPrice(),priceCur,
                          0,OrderStopLoss(),OrderTakeProfit(),
                          OrderComment(),OrderMagicNumber(),
                          (long)OrderOpenTime()*1000,(long)OrderExpiration()*1000);
}

//+------------------------------------------------------------------+
//| Handlers                                                         |
//+------------------------------------------------------------------+
void HandleSubscribe(const string id,const bool add)
{
   string list=CmdStr("symbols");
   string parts[];
   int n=StringSplit(list,',',parts);
   for(int i=0;i<n;i++)
   {
      string s=parts[i];
      if(s=="") continue;
      if(add)
      {
         if(!SymbolSelect(s,true))
         {
            Fleet_SendError(id,MTF_ERR_NOT_FOUND,GetLastError(),"unknown symbol: "+s,0);
            return;
         }
         AddSubscription(s);
      }
      else
         RemoveSubscription(s);
   }
   Fleet_SendAck(id);
}

void HandlePlaceOrder(const string id)
{
   string symbol   =CmdStr("symbol");
   int    type     =(int)Fleet_CmdInt("type");
   double volume   =Fleet_CmdDouble("volume");
   double price    =Fleet_CmdDouble("price");
   double sl       =Fleet_CmdDouble("stop_loss");
   double tp       =Fleet_CmdDouble("take_profit");
   int    deviation=(int)Fleet_CmdInt("deviation_points");
   long   magic    =Fleet_CmdInt("magic_number");
   string comment  =CmdStr("comment");
   long   expMs    =Fleet_CmdInt("expiration_unix_ms");
   string clientId =CmdStr("client_order_id");

   if(type<1 || type>6)
   {
      Fleet_SendError(id,(type==7||type==8)?MTF_ERR_UNSUPPORTED:MTF_ERR_INVALID_REQUEST,0,
                        "order type not supported on MT4",0);
      return;
   }
   int op=type-1; // proto = OP_* + 1
   int slippage=(deviation>0?deviation:10);

   RefreshRates();
   double execPrice=price;
   if(op==OP_BUY)  execPrice=MarketInfo(symbol,MODE_ASK);
   if(op==OP_SELL) execPrice=MarketInfo(symbol,MODE_BID);
   datetime expiration=(expMs>0?(datetime)(expMs/1000):0);

   int ticket=OrderSend(symbol,op,volume,execPrice,slippage,sl,tp,comment,(int)magic,expiration);
   if(ticket<0)
   {
      int err=GetLastError();
      Fleet_SendError(id,MapError4(err),err,ErrorDescription4(err),ErrorRetryable4(err)?1:0);
      return;
   }
   double filledPrice=execPrice,filledVol=volume;
   if(OrderSelect(ticket,SELECT_BY_TICKET))
   {
      filledPrice=OrderOpenPrice();
      filledVol  =OrderLots();
   }
   // MT4 has no separate position object: position ticket == order ticket.
   Fleet_SendOrderResult(id,ticket,ticket,filledPrice,filledVol,clientId);
}

void HandleClosePosition(const string id)
{
   long   ticket   =Fleet_CmdInt("ticket");
   double volume   =Fleet_CmdDouble("volume");
   int    deviation=(int)Fleet_CmdInt("deviation_points");

   if(!OrderSelect((int)ticket,SELECT_BY_TICKET) || OrderCloseTime()!=0 || OrderType()>OP_SELL)
   {
      Fleet_SendError(id,MTF_ERR_NOT_FOUND,GetLastError(),"open position not found",0);
      return;
   }
   RefreshRates();
   double lots =(volume>0?MathMin(volume,OrderLots()):OrderLots());
   double price=(OrderType()==OP_BUY?MarketInfo(OrderSymbol(),MODE_BID)
                                    :MarketInfo(OrderSymbol(),MODE_ASK));
   int slippage=(deviation>0?deviation:10);
   if(!OrderClose((int)ticket,lots,price,slippage))
   {
      int err=GetLastError();
      Fleet_SendError(id,MapError4(err),err,ErrorDescription4(err),ErrorRetryable4(err)?1:0);
      return;
   }
   Fleet_SendOrderResult(id,ticket,ticket,price,lots,"");
}

//+------------------------------------------------------------------+
//| Batch close: explicit tickets, or symbol/magic filter (empty =   |
//| close all). One CloseResult per target; the command itself       |
//| succeeds even when individual closes fail.                       |
//+------------------------------------------------------------------+
void HandleCloseBatch(const string id)
{
   string ticketsCsv=CmdStr("tickets");
   string fSymbol   =CmdStr("symbol");
   long   fMagic    =Fleet_CmdInt("magic_number");
   int    deviation =(int)Fleet_CmdInt("deviation_points");
   int    slippage  =(deviation>0?deviation:10);

   int targets[];
   if(ticketsCsv!="")
   {
      string parts[];
      int n=StringSplit(ticketsCsv,',',parts);
      for(int i=0;i<n;i++)
      {
         int t=(int)StringToInteger(parts[i]);
         if(t<=0) continue;
         int k=ArraySize(targets);
         ArrayResize(targets,k+1);
         targets[k]=t;
      }
   }
   else
   {
      // Snapshot tickets first: closing while iterating shifts indices.
      for(int i=OrdersTotal()-1;i>=0;i--)
      {
         if(!OrderSelect(i,SELECT_BY_POS)) continue;
         if(OrderType()>OP_SELL) continue;
         if(fSymbol!="" && OrderSymbol()!=fSymbol) continue;
         if(fMagic!=0 && OrderMagicNumber()!=fMagic) continue;
         int k=ArraySize(targets);
         ArrayResize(targets,k+1);
         targets[k]=OrderTicket();
      }
   }

   Fleet_BeginResult(id);
   for(int i=0;i<ArraySize(targets);i++)
      CloseOneForBatch(targets[i],slippage);
   Fleet_EndResult(PAYLOAD_BATCH_CLOSE);
}

// Fully closes one position, recording the outcome as a CloseResult row.
void CloseOneForBatch(const int ticket,const int slippage)
{
   if(!OrderSelect(ticket,SELECT_BY_TICKET) || OrderCloseTime()!=0 || OrderType()>OP_SELL)
   {
      Fleet_AddCloseResult(ticket,0,0,0,MTF_ERR_NOT_FOUND,GetLastError(),"open position not found",0);
      return;
   }
   RefreshRates();
   double lots =OrderLots();
   double price=(OrderType()==OP_BUY?MarketInfo(OrderSymbol(),MODE_BID)
                                    :MarketInfo(OrderSymbol(),MODE_ASK));
   if(!OrderClose(ticket,lots,price,slippage))
   {
      int err=GetLastError();
      Fleet_AddCloseResult(ticket,0,0,0,MapError4(err),err,ErrorDescription4(err),
                             ErrorRetryable4(err)?1:0);
      return;
   }
   Fleet_AddCloseResult(ticket,1,lots,price,0,0,"",0);
}

void HandleGetAccountInfo(const string id)
{
   double margin=AccountMargin();
   double level =(margin>0 ? AccountEquity()/margin*100.0 : 0);
   Fleet_SendAccountInfo(id,
      AccountInfoInteger(ACCOUNT_LOGIN),
      AccountInfoString(ACCOUNT_SERVER),
      AccountCompany(),AccountCurrency(),
      AccountBalance(),AccountEquity(),margin,AccountFreeMargin(),level,
      AccountLeverage(),(IsTradeAllowed()?1:0),AccountName(),
      (IsDemo()?0:2)); // MT4 only distinguishes demo/real (trade-mode 0/2)
}

void HandleGetHistory(const string id)
{
   string symbol=CmdStr("symbol");
   int    tf    =MapTimeframe((int)Fleet_CmdInt("timeframe"));
   long   fromMs=Fleet_CmdInt("from_unix_ms");
   long   toMs  =Fleet_CmdInt("to_unix_ms");
   int    count =(int)Fleet_CmdInt("count");
   if(tf==0)
   {
      Fleet_SendError(id,MTF_ERR_INVALID_REQUEST,0,"invalid timeframe",0);
      return;
   }
   datetime toT=(toMs>0?(datetime)(toMs/1000):TimeCurrent());
   MqlRates rates[];
   int copied=(count>0
               ? CopyRates(symbol,tf,toT,count,rates)
               : CopyRates(symbol,tf,(datetime)(fromMs/1000),toT,rates));
   if(copied<0)
   {
      Fleet_SendError(id,MTF_ERR_INTERNAL,GetLastError(),"CopyRates failed",1);
      return;
   }
   Fleet_BeginResult(id);
   for(int i=0;i<copied;i++)
      Fleet_AddBar((long)rates[i].time*1000,rates[i].open,rates[i].high,rates[i].low,
                     rates[i].close,rates[i].tick_volume,0,0);
   Fleet_EndResult(PAYLOAD_HISTORY);
}

void HandleGetPositions(const string id)
{
   string fSymbol=CmdStr("symbol");
   long   fMagic =Fleet_CmdInt("magic_number");
   Fleet_BeginResult(id);
   for(int i=0;i<OrdersTotal();i++)
   {
      if(!OrderSelect(i,SELECT_BY_POS)) continue;
      if(OrderType()>OP_SELL) continue;
      if(fSymbol!="" && OrderSymbol()!=fSymbol) continue;
      if(fMagic!=0 && OrderMagicNumber()!=fMagic) continue;
      double priceCur=(OrderType()==OP_BUY?MarketInfo(OrderSymbol(),MODE_BID)
                                          :MarketInfo(OrderSymbol(),MODE_ASK));
      Fleet_AddPosition(OrderTicket(),OrderSymbol(),OrderType()+1,OrderLots(),
                          OrderOpenPrice(),priceCur,OrderStopLoss(),OrderTakeProfit(),
                          OrderProfit(),OrderSwap(),OrderCommission(),OrderComment(),
                          OrderMagicNumber(),(long)OrderOpenTime()*1000);
   }
   Fleet_EndResult(PAYLOAD_POSITIONS);
}

void HandleGetPendingOrders(const string id)
{
   string fSymbol=CmdStr("symbol");
   long   fMagic =Fleet_CmdInt("magic_number");
   Fleet_BeginResult(id);
   for(int i=0;i<OrdersTotal();i++)
   {
      if(!OrderSelect(i,SELECT_BY_POS)) continue;
      if(OrderType()<=OP_SELL) continue;
      if(fSymbol!="" && OrderSymbol()!=fSymbol) continue;
      if(fMagic!=0 && OrderMagicNumber()!=fMagic) continue;
      double priceCur=(OrderType()==OP_BUYLIMIT||OrderType()==OP_BUYSTOP
                       ? MarketInfo(OrderSymbol(),MODE_ASK)
                       : MarketInfo(OrderSymbol(),MODE_BID));
      Fleet_AddOrder(OrderTicket(),OrderSymbol(),OrderType()+1,OrderLots(),OrderLots(),
                       OrderOpenPrice(),priceCur,0,OrderStopLoss(),OrderTakeProfit(),
                       OrderComment(),OrderMagicNumber(),(long)OrderOpenTime()*1000,
                       (long)OrderExpiration()*1000);
   }
   Fleet_EndResult(PAYLOAD_PENDING);
}

void HandleModifyPosition(const string id)
{
   long   ticket=Fleet_CmdInt("ticket");
   double sl    =Fleet_CmdDouble("stop_loss");
   double tp    =Fleet_CmdDouble("take_profit");
   if(!OrderSelect((int)ticket,SELECT_BY_TICKET) || OrderCloseTime()!=0 || OrderType()>OP_SELL)
   {
      Fleet_SendError(id,MTF_ERR_NOT_FOUND,GetLastError(),"open position not found",0);
      return;
   }
   double newSl=(sl<0?OrderStopLoss():sl);   // negative = keep
   double newTp=(tp<0?OrderTakeProfit():tp);
   if(!OrderModify((int)ticket,OrderOpenPrice(),newSl,newTp,OrderExpiration()))
   {
      int err=GetLastError();
      if(err==ERR_NO_RESULT) { Fleet_SendAck(id); return; } // nothing to change
      Fleet_SendError(id,MapError4(err),err,ErrorDescription4(err),ErrorRetryable4(err)?1:0);
      return;
   }
   Fleet_SendAck(id);
}

void HandleCancelOrder(const string id)
{
   long ticket=Fleet_CmdInt("ticket");
   if(!OrderSelect((int)ticket,SELECT_BY_TICKET) || OrderCloseTime()!=0 || OrderType()<=OP_SELL)
   {
      Fleet_SendError(id,MTF_ERR_NOT_FOUND,GetLastError(),"pending order not found",0);
      return;
   }
   if(!OrderDelete((int)ticket))
   {
      int err=GetLastError();
      Fleet_SendError(id,MapError4(err),err,ErrorDescription4(err),ErrorRetryable4(err)?1:0);
      return;
   }
   Fleet_SendAck(id);
}

void HandleModifyOrder(const string id)
{
   long   ticket=Fleet_CmdInt("ticket");
   double price =Fleet_CmdDouble("price");
   double sl    =Fleet_CmdDouble("stop_loss");
   double tp    =Fleet_CmdDouble("take_profit");
   long   expMs =Fleet_CmdInt("expiration_unix_ms");
   if(!OrderSelect((int)ticket,SELECT_BY_TICKET) || OrderCloseTime()!=0 || OrderType()<=OP_SELL)
   {
      Fleet_SendError(id,MTF_ERR_NOT_FOUND,GetLastError(),"pending order not found",0);
      return;
   }
   double newPrice=(price>0?price:OrderOpenPrice());
   double newSl   =(sl<0?OrderStopLoss():sl);
   double newTp   =(tp<0?OrderTakeProfit():tp);
   datetime newExp;
   if(expMs>0)      newExp=(datetime)(expMs/1000);
   else if(expMs==0)newExp=0;
   else             newExp=OrderExpiration(); // -1 keep
   if(!OrderModify((int)ticket,newPrice,newSl,newTp,newExp))
   {
      int err=GetLastError();
      if(err==ERR_NO_RESULT) { Fleet_SendAck(id); return; }
      Fleet_SendError(id,MapError4(err),err,ErrorDescription4(err),ErrorRetryable4(err)?1:0);
      return;
   }
   Fleet_SendAck(id);
}

void HandleGetSymbolInfo(const string id)
{
   string list=CmdStr("symbols");
   string parts[];
   int n=0;
   bool fromTree=false;
   if(list!="")
      n=StringSplit(list,',',parts);
   else
   {
      // No filter: the whole broker symbol tree, not just Market Watch
      // (the catalog sync wants everything the broker lists). Tree
      // symbols are read without SymbolSelect — forcing thousands into
      // Market Watch would hit the terminal's watch limit and subscribe
      // them all; SymbolInfo* reads work unselected on build 600+.
      fromTree=true;
      n=SymbolsTotal(false);
      ArrayResize(parts,n);
      for(int i=0;i<n;i++) parts[i]=SymbolName(i,false);
   }
   Fleet_BeginResult(id);
   for(int i=0;i<n;i++)
   {
      string s=parts[i];
      if(s=="") continue;
      if(!fromTree && !SymbolSelect(s,true)) continue;
      Fleet_AddSymbolInfo(s,
         SymbolInfoString(s,SYMBOL_DESCRIPTION),
         (int)SymbolInfoInteger(s,SYMBOL_DIGITS),
         SymbolInfoDouble(s,SYMBOL_POINT),
         SymbolInfoDouble(s,SYMBOL_TRADE_TICK_SIZE),
         SymbolInfoDouble(s,SYMBOL_TRADE_TICK_VALUE),
         SymbolInfoDouble(s,SYMBOL_TRADE_CONTRACT_SIZE),
         SymbolInfoDouble(s,SYMBOL_VOLUME_MIN),
         SymbolInfoDouble(s,SYMBOL_VOLUME_MAX),
         SymbolInfoDouble(s,SYMBOL_VOLUME_STEP),
         (int)SymbolInfoInteger(s,SYMBOL_TRADE_STOPS_LEVEL),
         (int)SymbolInfoInteger(s,SYMBOL_TRADE_FREEZE_LEVEL),
         SymbolInfoDouble(s,SYMBOL_MARGIN_INITIAL),
         SymbolInfoString(s,SYMBOL_CURRENCY_BASE),
         SymbolInfoString(s,SYMBOL_CURRENCY_PROFIT),
         SymbolInfoString(s,SYMBOL_CURRENCY_MARGIN),
         (SymbolInfoInteger(s,SYMBOL_TRADE_MODE)==SYMBOL_TRADE_MODE_FULL?1:0),
         SymbolInfoInteger(s,SYMBOL_SPREAD));
   }
   Fleet_EndResult(PAYLOAD_SYMBOLS);
}

void HandleGetDeals(const string id)
{
   long   fromMs =Fleet_CmdInt("from_unix_ms");
   long   toMs   =Fleet_CmdInt("to_unix_ms");
   string fSymbol=CmdStr("symbol");
   long   fMagic =Fleet_CmdInt("magic_number");
   datetime fromT=(datetime)(fromMs/1000);
   datetime toT  =(toMs>0?(datetime)(toMs/1000):TimeCurrent());

   Fleet_BeginResult(id);
   // MT4 has no deal objects: closed orders are mapped 1:1 to deals.
   for(int i=0;i<OrdersHistoryTotal();i++)
   {
      if(!OrderSelect(i,SELECT_BY_POS,MODE_HISTORY)) continue;
      if(OrderType()>OP_SELL) continue; // skip balance ops and pendings
      if(OrderCloseTime()<fromT || OrderCloseTime()>toT) continue;
      if(fSymbol!="" && OrderSymbol()!=fSymbol) continue;
      if(fMagic!=0 && OrderMagicNumber()!=fMagic) continue;
      Fleet_AddDealV3(OrderTicket(),OrderTicket(),OrderTicket(),OrderSymbol(),
                      OrderType()+1,OrderLots(),OrderClosePrice(),OrderProfit(),
                      OrderSwap(),OrderCommission(),OrderComment(),OrderMagicNumber(),
                      (long)OrderCloseTime()*1000,2,0);
   }
   Fleet_EndResult(PAYLOAD_DEALS);
}

//+------------------------------------------------------------------+
//| Helpers                                                          |
//+------------------------------------------------------------------+
string CmdStr(const string name)
{
   ushort buf[2048];
   int n=Fleet_CmdString(name,buf,2048);
   if(n<=0) return "";
   return ShortArrayToString(buf,0,n);
}

string BridgeLastError()
{
   ushort buf[512];
   int n=Fleet_LastError(buf,512);
   if(n<=0) return "(no detail)";
   return ShortArrayToString(buf,0,n);
}

int SymbolIndex(const string symbol)
{
   for(int i=0;i<ArraySize(g_symbols);i++)
      if(g_symbols[i]==symbol) return i;
   return -1;
}

void AddSubscription(const string symbol)
{
   int idx=SymbolIndex(symbol);
   if(idx>=0)
   {
      // Re-subscribe: reset the dedupe baseline so the next tick always flows.
      g_lastTickTime[idx]=0;
      g_lastBid[idx]=0;
      g_lastAsk[idx]=0;
      return;
   }
   int n=ArraySize(g_symbols);
   ArrayResize(g_symbols,n+1);
   ArrayResize(g_lastTickTime,n+1);
   ArrayResize(g_lastBid,n+1);
   ArrayResize(g_lastAsk,n+1);
   g_symbols[n]=symbol;
   g_lastTickTime[n]=0;
   g_lastBid[n]=0;
   g_lastAsk[n]=0;
}

void RemoveSubscription(const string symbol)
{
   int idx=SymbolIndex(symbol);
   if(idx<0) return;
   int n=ArraySize(g_symbols);
   for(int i=idx;i<n-1;i++)
   {
      g_symbols[i]=g_symbols[i+1];
      g_lastTickTime[i]=g_lastTickTime[i+1];
      g_lastBid[i]=g_lastBid[i+1];
      g_lastAsk[i]=g_lastAsk[i+1];
   }
   ArrayResize(g_symbols,n-1);
   ArrayResize(g_lastTickTime,n-1);
   ArrayResize(g_lastBid,n-1);
   ArrayResize(g_lastAsk,n-1);
}

int MapTimeframe(const int tf)
{
   switch(tf)
   {
      case 1: return PERIOD_M1;
      case 2: return PERIOD_M5;
      case 3: return PERIOD_M15;
      case 4: return PERIOD_M30;
      case 5: return PERIOD_H1;
      case 6: return PERIOD_H4;
      case 7: return PERIOD_D1;
      case 8: return PERIOD_W1;
      case 9: return PERIOD_MN1;
   }
   return 0;
}

bool ErrorRetryable4(const int err)
{
   switch(err)
   {
      case ERR_REQUOTE:
      case ERR_PRICE_CHANGED:
      case ERR_OFF_QUOTES:
      case ERR_TRADE_TIMEOUT:
      case ERR_BROKER_BUSY:
      case ERR_TRADE_CONTEXT_BUSY:
      case ERR_NO_CONNECTION:
      case ERR_TOO_FREQUENT_REQUESTS:
         return true;
   }
   return false;
}

int MapError4(const int err)
{
   switch(err)
   {
      case ERR_REQUOTE:               return MTF_ERR_REQUOTE;
      case ERR_PRICE_CHANGED:         return MTF_ERR_PRICE_CHANGED;
      case ERR_OFF_QUOTES:            return MTF_ERR_INVALID_PRICE;
      case ERR_INVALID_PRICE:         return MTF_ERR_INVALID_PRICE;
      case ERR_INVALID_STOPS:         return MTF_ERR_INVALID_STOPS;
      case ERR_INVALID_TRADE_VOLUME:  return MTF_ERR_INVALID_VOLUME;
      case ERR_NOT_ENOUGH_MONEY:      return MTF_ERR_NOT_ENOUGH_MONEY;
      case ERR_MARKET_CLOSED:         return MTF_ERR_MARKET_CLOSED;
      case ERR_TRADE_DISABLED:        return MTF_ERR_TRADE_DISABLED;
      case ERR_TRADE_TIMEOUT:         return MTF_ERR_TIMEOUT;
      case ERR_TOO_FREQUENT_REQUESTS: return MTF_ERR_TOO_MANY_REQUESTS;
      case ERR_NO_CONNECTION:         return MTF_ERR_CONNECTION_LOST;
      case ERR_INVALID_TICKET:        return MTF_ERR_NOT_FOUND;
   }
   return MTF_ERR_REJECTED;
}

string ErrorDescription4(const int err)
{
   return "MT4 error "+IntegerToString(err);
}
//+------------------------------------------------------------------+
