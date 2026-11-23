//+------------------------------------------------------------------+
//| Fleet.mq5 — Fleet engine connector for MetaTrader 5              |
//|                                                                  |
//| Rides FleetBridge.dll — a single, dependency-free native DLL   |
//| built from C# (shared/FleetBridge, NativeAOT win-x64) — which   |
//| owns the FIX session with the engine. This EA only:             |
//|   - pumps ticks for engine-subscribed symbols,                   |
//|   - polls commands and executes them on the terminal,            |
//|   - reports results and trade events back.                       |
//| Deploy: copy FleetBridge.dll into MQL5\Libraries (no other     |
//| files), enable 'Allow DLL imports', attach to any chart          |
//| (one instance per terminal).                                     |
//+------------------------------------------------------------------+
#property copyright "UltimateSoftwares"
#property version   "1.10"

input string InpEngineAddress = "127.0.0.1:50051"; // Engine host:port (fallback when no address file)
input bool   InpUseTls      = false;             // TLS to the engine
input int    InpTimerMs     = 100;               // Pump interval, ms

//--- bridge imports -------------------------------------------------
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
int    Fleet_BeginDepth(string symbol,long timeMs);
int    Fleet_AddDepthRow(int side,double price,double volume);
int    Fleet_EndDepth();
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

// mtfleet.v1.ErrorCode
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

// mtfleet.v1.TradeEvent.Kind
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
string g_terminalId="";   // resolved identity (input override or persisted GUID)
string g_engineAddress="";// resolved engine endpoint (address file or input)
string g_symbols[];       // engine-subscribed symbols
string g_depthSymbols[];  // engine-subscribed market-depth (DOM) symbols
long   g_lastTickMs[];    // per-symbol last pushed tick (msc)
double g_lastBid[];       // per-symbol last pushed bid
double g_lastAsk[];       // per-symbol last pushed ask
long   g_lastHeartbeat=0;

// Pending-order baseline for ORDER_MODIFIED detection. ORDER_UPDATE
// transactions also fire on placement/state changes, so we only emit
// an event when a tracked order's parameters actually changed.
long   g_ordTickets[];
double g_ordPrice[];
double g_ordStopLimit[];
double g_ordSL[];
double g_ordTP[];
long   g_ordExp[];        // expiration, epoch seconds (0 = GTC)

//--- terminal identity ----------------------------------------------
// Engine sessions are keyed by terminal_id. fleetd assigns a GUID when the
// admin provisions the terminal and stamps it into the data folder
// (MQL5\Files\mtfleet_terminal_id.txt; terminals run /portable), and the
// EA introduces itself with that id on connect. An unmanaged install
// without the file self-provisions a UUIDv4 once and persists it there.
#define MTF_ID_FILE "mtfleet_terminal_id.txt"

// Engine endpoint resolution: fleetd stamps the engine's TerminalService
// address into MQL5\Files\mtfleet_engine_address.txt (re-stamped on every
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

string ResolveTerminalId()
{
   int h=FileOpen(MTF_ID_FILE,FILE_READ|FILE_TXT|FILE_ANSI);
   if(h!=INVALID_HANDLE)
   {
      string id=TrimIdString(FileReadString(h));
      FileClose(h);
      if(StringLen(id)>0)
      {
         Print("Fleet: terminal id '",id,"' (MQL5\\Files\\",MTF_ID_FILE,")");
         return id;
      }
   }
   string fresh=GenerateTerminalGuid();
   h=FileOpen(MTF_ID_FILE,FILE_WRITE|FILE_TXT|FILE_ANSI);
   if(h!=INVALID_HANDLE)
   {
      FileWriteString(h,fresh);
      FileClose(h);
      Print("Fleet: generated terminal id '",fresh,"' (persisted in MQL5\\Files\\",MTF_ID_FILE,")");
   }
   else
      Print("Fleet: WARNING: generated terminal id '",fresh,
            "' but could not persist it (err ",GetLastError(),
            "); a new id will be generated next start");
   return fresh;
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
         Print("Fleet: engine address '",addr,"' (MQL5\\Files\\",MTF_ENGINE_FILE,")");
         return addr;
      }
   }
   return InpEngineAddress;
}

//+------------------------------------------------------------------+
// ACCOUNT_MARGIN_MODE mapped to fleet.v1.MarginMode for the hello.
int FleetMarginMode()
{
   switch((int)AccountInfoInteger(ACCOUNT_MARGIN_MODE))
   {
      case ACCOUNT_MARGIN_MODE_RETAIL_NETTING: return 1;
      case ACCOUNT_MARGIN_MODE_RETAIL_HEDGING: return 2;
      case ACCOUNT_MARGIN_MODE_EXCHANGE:       return 3;
   }
   return 0;
}

//+------------------------------------------------------------------+
// One connector per terminal: the startup chart ([StartUp] in the
// start-config ini) and a restored profile chart can both carry Fleet, and
// a second Fleet_Init would restart the shared bridge. The lowest chart id
// wins; a losing startup twin (same symbol/period) closes its chart too.
bool g_bridgeOwner=false; // only the instance that called Fleet_Init shuts the shared bridge down

bool FleetDuplicate()
{
   string me=MQLInfoString(MQL_PROGRAM_NAME);
   long self=ChartID();
   bool twin=false;
   for(long id=ChartFirst(); id>=0; id=ChartNext(id))
   {
      if(id==self || ChartGetString(id,CHART_EXPERT_NAME)!=me)
         continue;
      if(id>self)
         continue;
      if(ChartSymbol(id)==_Symbol && ChartPeriod(id)==_Period)
         twin=true;
      Print("Fleet: connector already running on chart ",id," (",ChartSymbol(id),"); not starting a second one");
      if(twin)
         ChartClose(self);
      return true;
   }
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
   int tz=(int)((TimeTradeServer()-TimeGMT())/60);
   g_engineAddress=ResolveEngineAddress();
   g_bridgeOwner=true;
   int rc=Fleet_Init(g_engineAddress,g_terminalId,2 /*PLATFORM_MT5*/,
                       IntegerToString(TerminalInfoInteger(TERMINAL_BUILD)),
                       AccountInfoInteger(ACCOUNT_LOGIN),
                       AccountInfoString(ACCOUNT_SERVER),tz,InpUseTls?1:0,
                       FleetMarginMode(),AccountInfoString(ACCOUNT_COMPANY));
   if(rc!=0)
   {
      Print("Fleet: bridge init failed (rc=",rc,"): ",BridgeLastError());
      Print("Fleet: verify MQL5\\Libraries contains the 64-bit FleetBridge.dll ",
            "(cargo build --release -p fleet-bridge --target x86_64-pc-windows-gnu)");
      return INIT_FAILED;
   }
   SnapshotPendingBaseline(); // no ORDER_MODIFIED for pre-existing pendings
   EventSetMillisecondTimer(MathMax(InpTimerMs,20));
   Print("Fleet: connector started, engine=",g_engineAddress,", terminal_id=",g_terminalId);
   PumpBridgeLogs();
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
   EventKillTimer();
   for(int i=0;i<ArraySize(g_depthSymbols);i++)
      MarketBookRelease(g_depthSymbols[i]);
   if(g_bridgeOwner)
      Fleet_Shutdown();
   g_bridgeOwner=false;
}

void OnTick()
{
   // Low-latency path for the chart symbol; other symbols ride OnTimer.
   PushSymbolTick(_Symbol);
}

//+------------------------------------------------------------------+
//| Market depth: fires on every book change for MarketBookAdd'ed     |
//| symbols. The full snapshot ships immediately (zero latency),      |
//| exactly as the terminal orders it (asks first, then bids).        |
//+------------------------------------------------------------------+
void OnBookEvent(const string &symbol)
{
   if(DepthIndex(symbol)<0) return;
   MqlBookInfo book[];
   if(!MarketBookGet(symbol,book)) return;
   Fleet_BeginDepth(symbol,0); // 0 = bridge stamps send time
   for(int i=0;i<ArraySize(book);i++)
   {
      int side=0; // mtfleet.v1.BookSide
      if(book[i].type==BOOK_TYPE_SELL || book[i].type==BOOK_TYPE_SELL_MARKET) side=1;
      else if(book[i].type==BOOK_TYPE_BUY || book[i].type==BOOK_TYPE_BUY_MARKET) side=2;
      double vol=(book[i].volume_real>0.0 ? book[i].volume_real : (double)book[i].volume);
      Fleet_AddDepthRow(side,book[i].price,vol);
   }
   Fleet_EndDepth();
}

void OnTimer()
{
   PumpBridgeLogs();
   for(int i=0;i<ArraySize(g_symbols);i++)
      PushSymbolTick(g_symbols[i]);
   PumpCommands();
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
//| Trade events -> engine                                           |
//+------------------------------------------------------------------+
int OrdIndexOf(const long ticket)
{
   for(int i=0;i<ArraySize(g_ordTickets);i++)
      if(g_ordTickets[i]==ticket) return i;
   return -1;
}

// Insert or update a pending order's tracked parameters.
void OrdRemember(const long ticket,const double price,const double stopLimit,
                 const double sl,const double tp,const long expSec)
{
   int i=OrdIndexOf(ticket);
   if(i<0)
   {
      i=ArraySize(g_ordTickets);
      ArrayResize(g_ordTickets,i+1);
      ArrayResize(g_ordPrice,i+1);
      ArrayResize(g_ordStopLimit,i+1);
      ArrayResize(g_ordSL,i+1);
      ArrayResize(g_ordTP,i+1);
      ArrayResize(g_ordExp,i+1);
      g_ordTickets[i]=ticket;
   }
   g_ordPrice[i]=price;
   g_ordStopLimit[i]=stopLimit;
   g_ordSL[i]=sl;
   g_ordTP[i]=tp;
   g_ordExp[i]=expSec;
}

void OrdForget(const long ticket)
{
   int i=OrdIndexOf(ticket);
   if(i<0) return;
   int last=ArraySize(g_ordTickets)-1;
   g_ordTickets[i]=g_ordTickets[last];
   g_ordPrice[i]=g_ordPrice[last];
   g_ordStopLimit[i]=g_ordStopLimit[last];
   g_ordSL[i]=g_ordSL[last];
   g_ordTP[i]=g_ordTP[last];
   g_ordExp[i]=g_ordExp[last];
   ArrayResize(g_ordTickets,last);
   ArrayResize(g_ordPrice,last);
   ArrayResize(g_ordStopLimit,last);
   ArrayResize(g_ordSL,last);
   ArrayResize(g_ordTP,last);
   ArrayResize(g_ordExp,last);
}

// Records the currently selected order (OrderGet* context) as baseline.
void OrdRememberSelected(const long ticket)
{
   OrdRemember(ticket,
               OrderGetDouble(ORDER_PRICE_OPEN),
               OrderGetDouble(ORDER_PRICE_STOPLIMIT),
               OrderGetDouble(ORDER_SL),
               OrderGetDouble(ORDER_TP),
               (long)OrderGetInteger(ORDER_TIME_EXPIRATION));
}

// True for pending order types (limits/stops/stop-limits); market orders
// pass through the order list transiently while executing and are skipped.
bool IsPendingType(const long t)
{
   return t>=ORDER_TYPE_BUY_LIMIT && t<=ORDER_TYPE_SELL_STOP_LIMIT;
}

void SnapshotPendingBaseline()
{
   ArrayResize(g_ordTickets,0);
   ArrayResize(g_ordPrice,0);
   ArrayResize(g_ordStopLimit,0);
   ArrayResize(g_ordSL,0);
   ArrayResize(g_ordTP,0);
   ArrayResize(g_ordExp,0);
   for(int i=0;i<OrdersTotal();i++)
   {
      ulong t=OrderGetTicket(i);
      if(t==0) continue;
      if(!IsPendingType(OrderGetInteger(ORDER_TYPE))) continue;
      OrdRememberSelected((long)t);
   }
}

// Pushes the currently selected (active) order as an order event.
void PushSelectedOrderEvent(const int kind,const long ticket)
{
   Fleet_PushOrderEvent(kind,ticket,
                          OrderGetString(ORDER_SYMBOL),
                          (int)OrderGetInteger(ORDER_TYPE)+1,
                          OrderGetDouble(ORDER_VOLUME_INITIAL),
                          OrderGetDouble(ORDER_VOLUME_CURRENT),
                          OrderGetDouble(ORDER_PRICE_OPEN),
                          OrderGetDouble(ORDER_PRICE_CURRENT),
                          OrderGetDouble(ORDER_PRICE_STOPLIMIT),
                          OrderGetDouble(ORDER_SL),
                          OrderGetDouble(ORDER_TP),
                          OrderGetString(ORDER_COMMENT),
                          OrderGetInteger(ORDER_MAGIC),
                          (long)OrderGetInteger(ORDER_TIME_SETUP_MSC),
                          (long)OrderGetInteger(ORDER_TIME_EXPIRATION)*1000);
}

void OnTradeTransaction(const MqlTradeTransaction &trans,
                        const MqlTradeRequest &request,
                        const MqlTradeResult &result)
{
   // Pending-order lifecycle: track parameters so ORDER_UPDATE noise
   // (placement/state transitions) doesn't masquerade as a modify.
   if(trans.type==TRADE_TRANSACTION_ORDER_ADD)
   {
      if(!OrderSelect(trans.order)) return;
      if(!IsPendingType(OrderGetInteger(ORDER_TYPE))) return; // executing market order
      OrdRememberSelected((long)trans.order);
      PushSelectedOrderEvent(EVT_ORDER_PLACED,(long)trans.order);
      return;
   }
   if(trans.type==TRADE_TRANSACTION_ORDER_DELETE)
   {
      OrdForget((long)trans.order);
      if(!HistoryOrderSelect(trans.order)) return;
      if(!IsPendingType(HistoryOrderGetInteger(trans.order,ORDER_TYPE))) return;
      long state=HistoryOrderGetInteger(trans.order,ORDER_STATE);
      int kind;
      if(state==ORDER_STATE_CANCELED)     kind=EVT_ORDER_CANCELLED;
      else if(state==ORDER_STATE_EXPIRED) kind=EVT_ORDER_EXPIRED;
      else if(state==ORDER_STATE_FILLED)  kind=EVT_ORDER_FILLED; // the fill itself also arrives as POSITION_OPENED via DEAL_ADD
      else return;
      Fleet_PushOrderEvent(kind,(long)trans.order,
                             HistoryOrderGetString(trans.order,ORDER_SYMBOL),
                             (int)HistoryOrderGetInteger(trans.order,ORDER_TYPE)+1,
                             HistoryOrderGetDouble(trans.order,ORDER_VOLUME_INITIAL),
                             HistoryOrderGetDouble(trans.order,ORDER_VOLUME_CURRENT),
                             HistoryOrderGetDouble(trans.order,ORDER_PRICE_OPEN),
                             HistoryOrderGetDouble(trans.order,ORDER_PRICE_CURRENT),
                             HistoryOrderGetDouble(trans.order,ORDER_PRICE_STOPLIMIT),
                             HistoryOrderGetDouble(trans.order,ORDER_SL),
                             HistoryOrderGetDouble(trans.order,ORDER_TP),
                             HistoryOrderGetString(trans.order,ORDER_COMMENT),
                             HistoryOrderGetInteger(trans.order,ORDER_MAGIC),
                             (long)HistoryOrderGetInteger(trans.order,ORDER_TIME_SETUP_MSC),
                             (long)HistoryOrderGetInteger(trans.order,ORDER_TIME_EXPIRATION)*1000);
      return;
   }
   if(trans.type==TRADE_TRANSACTION_ORDER_UPDATE)
   {
      if(!OrderSelect(trans.order)) return; // already filled/removed
      if(!IsPendingType(OrderGetInteger(ORDER_TYPE))) return;
      double price    =OrderGetDouble(ORDER_PRICE_OPEN);
      double stopLimit=OrderGetDouble(ORDER_PRICE_STOPLIMIT);
      double sl       =OrderGetDouble(ORDER_SL);
      double tp       =OrderGetDouble(ORDER_TP);
      long   expSec   =(long)OrderGetInteger(ORDER_TIME_EXPIRATION);
      int i=OrdIndexOf((long)trans.order);
      if(i<0)
      {
         OrdRemember((long)trans.order,price,stopLimit,sl,tp,expSec); // first sight = placement
         return;
      }
      bool changed=(price!=g_ordPrice[i] || stopLimit!=g_ordStopLimit[i] ||
                    sl!=g_ordSL[i] || tp!=g_ordTP[i] || expSec!=g_ordExp[i]);
      OrdRemember((long)trans.order,price,stopLimit,sl,tp,expSec);
      if(!changed) return;
      PushSelectedOrderEvent(EVT_ORDER_MODIFIED,(long)trans.order);
      return;
   }

   // Position property change (manual or programmatic SL/TP edit, partial fill
   // volume updates, ...). No deal is created, so handle it before the deal path.
   if(trans.type==TRADE_TRANSACTION_POSITION)
   {
      if(!PositionSelectByTicket(trans.position)) return;
      Fleet_PushPositionEvent(EVT_POSITION_MODIFIED,(long)trans.position,
                                PositionGetString(POSITION_SYMBOL),
                                (int)PositionGetInteger(POSITION_TYPE)+1,
                                PositionGetDouble(POSITION_VOLUME),
                                PositionGetDouble(POSITION_PRICE_OPEN),
                                PositionGetDouble(POSITION_PRICE_CURRENT),
                                PositionGetDouble(POSITION_SL),
                                PositionGetDouble(POSITION_TP),
                                PositionGetDouble(POSITION_PROFIT),
                                PositionGetDouble(POSITION_SWAP),
                                0.0,
                                PositionGetString(POSITION_COMMENT),
                                PositionGetInteger(POSITION_MAGIC),
                                (long)PositionGetInteger(POSITION_TIME_MSC));
      return;
   }

   if(trans.type!=TRADE_TRANSACTION_DEAL_ADD) return;
   if(!HistoryDealSelect(trans.deal)) return;

   long entry=HistoryDealGetInteger(trans.deal,DEAL_ENTRY);
   if(HistoryDealGetInteger(trans.deal,DEAL_TYPE)>DEAL_TYPE_SELL) return; // balance ops etc.

   int  kind=(entry==DEAL_ENTRY_IN ? EVT_POSITION_OPENED : EVT_POSITION_CLOSED);
   long posTicket=(long)HistoryDealGetInteger(trans.deal,DEAL_POSITION_ID);
   string symbol=HistoryDealGetString(trans.deal,DEAL_SYMBOL);

   double priceCur=0, slCur=0, tpCur=0, volCur=0, profitCur=0, swapCur=0;
   long   openMs=0;
   int    ptype=(int)HistoryDealGetInteger(trans.deal,DEAL_TYPE)+1;
   if(PositionSelectByTicket(posTicket))
   {
      priceCur =PositionGetDouble(POSITION_PRICE_CURRENT);
      slCur    =PositionGetDouble(POSITION_SL);
      tpCur    =PositionGetDouble(POSITION_TP);
      volCur   =PositionGetDouble(POSITION_VOLUME);
      profitCur=PositionGetDouble(POSITION_PROFIT);
      swapCur  =PositionGetDouble(POSITION_SWAP);
      openMs   =(long)PositionGetInteger(POSITION_TIME_MSC);
      ptype    =(int)PositionGetInteger(POSITION_TYPE)+1;
   }
   else
   {
      volCur   =HistoryDealGetDouble(trans.deal,DEAL_VOLUME);
      profitCur=HistoryDealGetDouble(trans.deal,DEAL_PROFIT);
      swapCur  =HistoryDealGetDouble(trans.deal,DEAL_SWAP);
      priceCur =HistoryDealGetDouble(trans.deal,DEAL_PRICE);
      openMs   =(long)HistoryDealGetInteger(trans.deal,DEAL_TIME_MSC);
   }

   Fleet_PushPositionEvent(kind,posTicket,symbol,ptype,volCur,
                             HistoryDealGetDouble(trans.deal,DEAL_PRICE),priceCur,slCur,tpCur,
                             profitCur,swapCur,HistoryDealGetDouble(trans.deal,DEAL_COMMISSION),
                             HistoryDealGetString(trans.deal,DEAL_COMMENT),
                             HistoryDealGetInteger(trans.deal,DEAL_MAGIC),openMs);
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
   // Identity dedupe (same as MT4): skip only an exact repeat of the last
   // pushed tick. A monotonic time_msc high-water mark is NOT safe here —
   // broker failovers/clock steps can move time_msc backwards, which would
   // latch the guard shut and silently mute the symbol forever.
   if(t.time_msc==g_lastTickMs[idx] && t.bid==g_lastBid[idx] && t.ask==g_lastAsk[idx]) return;
   g_lastTickMs[idx]=t.time_msc;
   g_lastBid[idx]=t.bid;
   g_lastAsk[idx]=t.ask;
   int spread=(int)SymbolInfoInteger(symbol,SYMBOL_SPREAD); // points, incl. fractional pips
   Fleet_PushTick(symbol,t.bid,t.ask,t.last,(long)t.volume,t.time_msc,spread,
                    AccountInfoDouble(ACCOUNT_BALANCE),AccountInfoDouble(ACCOUNT_EQUITY),
                    AccountInfoDouble(ACCOUNT_MARGIN),AccountInfoDouble(ACCOUNT_MARGIN_FREE));
}

void MaybeHeartbeat()
{
   long iv=Fleet_HeartbeatIntervalMs();
   if(iv<=0) iv=15000;
   long now=(long)TimeGMT()*1000;
   if(now-g_lastHeartbeat < iv/2) return;
   g_lastHeartbeat=now;
   Fleet_BeginHeartbeat(TerminalInfoInteger(TERMINAL_CONNECTED)?1:0,
                          AccountInfoDouble(ACCOUNT_EQUITY),
                          AccountInfoDouble(ACCOUNT_BALANCE),
                          OrdersTotal());
   // Full open-position snapshot (floating PnL included) rides in every
   // heartbeat so the engine mirrors exposure without polling get_positions.
   for(int i=0;i<PositionsTotal();i++)
   {
      ulong ticket=PositionGetTicket(i);
      if(ticket==0) continue;
      Fleet_AddPosition((long)ticket,
         PositionGetString(POSITION_SYMBOL),
         (int)PositionGetInteger(POSITION_TYPE)+1,
         PositionGetDouble(POSITION_VOLUME),
         PositionGetDouble(POSITION_PRICE_OPEN),
         PositionGetDouble(POSITION_PRICE_CURRENT),
         PositionGetDouble(POSITION_SL),
         PositionGetDouble(POSITION_TP),
         PositionGetDouble(POSITION_PROFIT),
         PositionGetDouble(POSITION_SWAP),
         0.0, // MT5 reports commission on deals, not positions
         PositionGetString(POSITION_COMMENT),
         PositionGetInteger(POSITION_MAGIC),
         (long)PositionGetInteger(POSITION_TIME_MSC));
   }
   Fleet_EndHeartbeat();
}

void PumpCommands()
{
   ushort idBuf[64];
   for(int i=0;i<32;i++) // bound work per timer slice
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
      case CMD_SUBSCRIBE:          HandleSubscribe(id,true);     break;
      case CMD_UNSUBSCRIBE:        HandleSubscribe(id,false);    break;
      case CMD_PLACE_ORDER:        HandlePlaceOrder(id);         break;
      case CMD_CLOSE_POSITION:     HandleClosePosition(id);      break;
      case CMD_GET_ACCOUNT_INFO:   HandleGetAccountInfo(id);     break;
      case CMD_GET_HISTORY:        HandleGetHistory(id);         break;
      case CMD_GET_POSITIONS:      HandleGetPositions(id);       break;
      case CMD_GET_PENDING_ORDERS: HandleGetPendingOrders(id);   break;
      case CMD_MODIFY_POSITION:    HandleModifyPosition(id);     break;
      case CMD_CANCEL_ORDER:       HandleCancelOrder(id);        break;
      case CMD_MODIFY_ORDER:       HandleModifyOrder(id);        break;
      case CMD_GET_SYMBOL_INFO:    HandleGetSymbolInfo(id);      break;
      case CMD_GET_DEALS_HISTORY:  HandleGetDeals(id);           break;
      case CMD_CLOSE_POSITIONS_BATCH: HandleCloseBatch(id);      break;
      case CMD_SUBSCRIBE_DEPTH:    HandleDepthSubscribe(id,true);  break;
      case CMD_UNSUBSCRIBE_DEPTH:  HandleDepthSubscribe(id,false); break;
      default:
         Fleet_SendError(id,MTF_ERR_UNSUPPORTED,0,"unknown command type",0);
   }
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
            Fleet_SendError(id,MTF_ERR_NOT_FOUND,(int)GetLastError(),"unknown symbol: "+s,0);
            return;
         }
         AddSubscription(s);
      }
      else
         RemoveSubscription(s);
   }
   Fleet_SendAck(id);
}

void HandleDepthSubscribe(const string id,const bool add)
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
            Fleet_SendError(id,MTF_ERR_NOT_FOUND,(int)GetLastError(),"unknown symbol: "+s,0);
            return;
         }
         if(!MarketBookAdd(s))
         {
            Fleet_SendError(id,MTF_ERR_UNSUPPORTED,(int)GetLastError(),
                              "market depth unavailable for "+s+" (broker does not publish a book?)",0);
            return;
         }
         AddDepthSubscription(s);
      }
      else
      {
         if(DepthIndex(s)>=0)
         {
            MarketBookRelease(s);
            RemoveDepthSubscription(s);
         }
      }
   }
   Fleet_SendAck(id);
}

void HandlePlaceOrder(const string id)
{
   string symbol   =CmdStr("symbol");
   int    type     =(int)Fleet_CmdInt("type");
   double volume   =Fleet_CmdDouble("volume");
   double price    =Fleet_CmdDouble("price");
   double stopLimit=Fleet_CmdDouble("stop_limit_price");
   double sl       =Fleet_CmdDouble("stop_loss");
   double tp       =Fleet_CmdDouble("take_profit");
   int    deviation=(int)Fleet_CmdInt("deviation_points");
   long   magic    =Fleet_CmdInt("magic_number");
   string comment  =CmdStr("comment");
   long   expMs    =Fleet_CmdInt("expiration_unix_ms");
   string clientId =CmdStr("client_order_id");

   if(type<1 || type>8)
   {
      Fleet_SendError(id,MTF_ERR_INVALID_REQUEST,0,"invalid order type",0);
      return;
   }
   ENUM_ORDER_TYPE ot=(ENUM_ORDER_TYPE)(type-1); // proto = MT5 enum + 1

   MqlTradeRequest req;
   MqlTradeResult  res;
   ZeroMemory(req);
   ZeroMemory(res);
   req.symbol   =symbol;
   req.volume   =volume;
   req.magic    =magic;
   req.comment  =comment;
   req.deviation=(deviation>0?deviation:10);
   req.type     =ot;
   req.sl       =sl;
   req.tp       =tp;

   if(ot==ORDER_TYPE_BUY || ot==ORDER_TYPE_SELL)
   {
      MqlTick t;
      if(!SymbolInfoTick(symbol,t))
      {
         Fleet_SendError(id,MTF_ERR_NOT_FOUND,(int)GetLastError(),"no quote for symbol: "+symbol,0);
         return;
      }
      req.action      =TRADE_ACTION_DEAL;
      req.price       =(ot==ORDER_TYPE_BUY?t.ask:t.bid);
      req.type_filling=FillingMode(symbol);
   }
   else
   {
      req.action      =TRADE_ACTION_PENDING;
      req.price       =price;
      req.type_filling=ORDER_FILLING_RETURN;
      if(ot==ORDER_TYPE_BUY_STOP_LIMIT || ot==ORDER_TYPE_SELL_STOP_LIMIT)
         req.stoplimit=stopLimit;
      if(expMs>0)
      {
         req.type_time =ORDER_TIME_SPECIFIED;
         req.expiration=(datetime)(expMs/1000);
      }
      else
         req.type_time=ORDER_TIME_GTC;
   }

   if(!OrderSend(req,res) || !RetcodeOk(res.retcode))
   {
      Fleet_SendError(id,MapRetcode(res.retcode),(int)res.retcode,res.comment,
                        RetcodeRetryable(res.retcode)?1:0);
      return;
   }

   long posTicket=0;
   if(res.deal>0 && HistoryDealSelect(res.deal))
      posTicket=(long)HistoryDealGetInteger(res.deal,DEAL_POSITION_ID);
   Fleet_SendOrderResult(id,(long)res.order,posTicket,res.price,res.volume,clientId);
}

void HandleClosePosition(const string id)
{
   long   ticket   =Fleet_CmdInt("ticket");
   double volume   =Fleet_CmdDouble("volume");
   int    deviation=(int)Fleet_CmdInt("deviation_points");

   if(!PositionSelectByTicket((ulong)ticket))
   {
      Fleet_SendError(id,MTF_ERR_NOT_FOUND,(int)GetLastError(),"position not found",0);
      return;
   }
   string symbol =PositionGetString(POSITION_SYMBOL);
   long   ptype  =PositionGetInteger(POSITION_TYPE);
   double posVol =PositionGetDouble(POSITION_VOLUME);
   double closeVol=(volume>0 ? MathMin(volume,posVol) : posVol);

   MqlTick t;
   if(!SymbolInfoTick(symbol,t))
   {
      Fleet_SendError(id,MTF_ERR_INTERNAL,(int)GetLastError(),"no quote for symbol: "+symbol,1);
      return;
   }

   MqlTradeRequest req;
   MqlTradeResult  res;
   ZeroMemory(req);
   ZeroMemory(res);
   req.action      =TRADE_ACTION_DEAL;
   req.position    =(ulong)ticket;
   req.symbol      =symbol;
   req.volume      =closeVol;
   req.deviation   =(deviation>0?deviation:10);
   req.type        =(ptype==POSITION_TYPE_BUY?ORDER_TYPE_SELL:ORDER_TYPE_BUY);
   req.price       =(req.type==ORDER_TYPE_SELL?t.bid:t.ask);
   req.type_filling=FillingMode(symbol);

   if(!OrderSend(req,res) || !RetcodeOk(res.retcode))
   {
      Fleet_SendError(id,MapRetcode(res.retcode),(int)res.retcode,res.comment,
                        RetcodeRetryable(res.retcode)?1:0);
      return;
   }
   Fleet_SendOrderResult(id,(long)res.order,ticket,res.price,res.volume,"");
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

   long targets[];
   if(ticketsCsv!="")
   {
      string parts[];
      int n=StringSplit(ticketsCsv,',',parts);
      for(int i=0;i<n;i++)
      {
         long t=StringToInteger(parts[i]);
         if(t<=0) continue;
         int k=ArraySize(targets);
         ArrayResize(targets,k+1);
         targets[k]=t;
      }
   }
   else
   {
      // Snapshot tickets first: closing while iterating shifts indices.
      for(int i=PositionsTotal()-1;i>=0;i--)
      {
         ulong t=PositionGetTicket(i);
         if(t==0) continue;
         if(fSymbol!="" && PositionGetString(POSITION_SYMBOL)!=fSymbol) continue;
         if(fMagic!=0 && PositionGetInteger(POSITION_MAGIC)!=fMagic) continue;
         int k=ArraySize(targets);
         ArrayResize(targets,k+1);
         targets[k]=(long)t;
      }
   }

   Fleet_BeginResult(id);
   for(int i=0;i<ArraySize(targets);i++)
      CloseOneForBatch(targets[i],deviation);
   Fleet_EndResult(PAYLOAD_BATCH_CLOSE);
}

// Fully closes one position, recording the outcome as a CloseResult row.
void CloseOneForBatch(const long ticket,const int deviation)
{
   if(!PositionSelectByTicket((ulong)ticket))
   {
      Fleet_AddCloseResult(ticket,0,0,0,MTF_ERR_NOT_FOUND,(int)GetLastError(),"position not found",0);
      return;
   }
   string symbol=PositionGetString(POSITION_SYMBOL);
   long   ptype =PositionGetInteger(POSITION_TYPE);
   double vol   =PositionGetDouble(POSITION_VOLUME);

   MqlTick t;
   if(!SymbolInfoTick(symbol,t))
   {
      Fleet_AddCloseResult(ticket,0,0,0,MTF_ERR_INTERNAL,(int)GetLastError(),"no quote for symbol: "+symbol,1);
      return;
   }

   MqlTradeRequest req;
   MqlTradeResult  res;
   ZeroMemory(req);
   ZeroMemory(res);
   req.action      =TRADE_ACTION_DEAL;
   req.position    =(ulong)ticket;
   req.symbol      =symbol;
   req.volume      =vol;
   req.deviation   =(deviation>0?deviation:10);
   req.type        =(ptype==POSITION_TYPE_BUY?ORDER_TYPE_SELL:ORDER_TYPE_BUY);
   req.price       =(req.type==ORDER_TYPE_SELL?t.bid:t.ask);
   req.type_filling=FillingMode(symbol);

   if(!OrderSend(req,res) || !RetcodeOk(res.retcode))
   {
      Fleet_AddCloseResult(ticket,0,0,0,MapRetcode(res.retcode),(int)res.retcode,res.comment,
                             RetcodeRetryable(res.retcode)?1:0);
      return;
   }
   Fleet_AddCloseResult(ticket,1,res.volume,res.price,0,0,"",0);
}

void HandleGetAccountInfo(const string id)
{
   Fleet_SendAccountInfo(id,
      AccountInfoInteger(ACCOUNT_LOGIN),
      AccountInfoString(ACCOUNT_SERVER),
      AccountInfoString(ACCOUNT_COMPANY),
      AccountInfoString(ACCOUNT_CURRENCY),
      AccountInfoDouble(ACCOUNT_BALANCE),
      AccountInfoDouble(ACCOUNT_EQUITY),
      AccountInfoDouble(ACCOUNT_MARGIN),
      AccountInfoDouble(ACCOUNT_MARGIN_FREE),
      AccountInfoDouble(ACCOUNT_MARGIN_LEVEL),
      (int)AccountInfoInteger(ACCOUNT_LEVERAGE),
      (AccountInfoInteger(ACCOUNT_TRADE_ALLOWED)?1:0),
      AccountInfoString(ACCOUNT_NAME),
      (int)AccountInfoInteger(ACCOUNT_TRADE_MODE));
}

void HandleGetHistory(const string id)
{
   string symbol=CmdStr("symbol");
   ENUM_TIMEFRAMES tf=MapTimeframe((int)Fleet_CmdInt("timeframe"));
   long   fromMs=Fleet_CmdInt("from_unix_ms");
   long   toMs  =Fleet_CmdInt("to_unix_ms");
   int    count =(int)Fleet_CmdInt("count");
   if(tf==PERIOD_CURRENT)
   {
      Fleet_SendError(id,MTF_ERR_INVALID_REQUEST,0,"invalid timeframe",0);
      return;
   }
   datetime toT=(toMs>0 ? (datetime)(toMs/1000) : TimeCurrent());
   MqlRates rates[];
   int copied=(count>0
               ? CopyRates(symbol,tf,toT,count,rates)
               : CopyRates(symbol,tf,(datetime)(fromMs/1000),toT,rates));
   if(copied<0)
   {
      Fleet_SendError(id,MTF_ERR_INTERNAL,(int)GetLastError(),"CopyRates failed",1);
      return;
   }
   Fleet_BeginResult(id);
   for(int i=0;i<copied;i++)
      Fleet_AddBar((long)rates[i].time*1000,rates[i].open,rates[i].high,rates[i].low,
                     rates[i].close,rates[i].tick_volume,rates[i].real_volume,rates[i].spread);
   Fleet_EndResult(PAYLOAD_HISTORY);
}

void HandleGetPositions(const string id)
{
   string fSymbol=CmdStr("symbol");
   long   fMagic =Fleet_CmdInt("magic_number");
   Fleet_BeginResult(id);
   for(int i=0;i<PositionsTotal();i++)
   {
      ulong ticket=PositionGetTicket(i);
      if(ticket==0) continue;
      if(fSymbol!="" && PositionGetString(POSITION_SYMBOL)!=fSymbol) continue;
      if(fMagic!=0 && PositionGetInteger(POSITION_MAGIC)!=fMagic) continue;
      Fleet_AddPosition((long)ticket,
         PositionGetString(POSITION_SYMBOL),
         (int)PositionGetInteger(POSITION_TYPE)+1,
         PositionGetDouble(POSITION_VOLUME),
         PositionGetDouble(POSITION_PRICE_OPEN),
         PositionGetDouble(POSITION_PRICE_CURRENT),
         PositionGetDouble(POSITION_SL),
         PositionGetDouble(POSITION_TP),
         PositionGetDouble(POSITION_PROFIT),
         PositionGetDouble(POSITION_SWAP),
         0.0, // MT5 reports commission on deals, not positions
         PositionGetString(POSITION_COMMENT),
         PositionGetInteger(POSITION_MAGIC),
         (long)PositionGetInteger(POSITION_TIME_MSC));
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
      ulong ticket=OrderGetTicket(i);
      if(ticket==0) continue;
      if(fSymbol!="" && OrderGetString(ORDER_SYMBOL)!=fSymbol) continue;
      if(fMagic!=0 && OrderGetInteger(ORDER_MAGIC)!=fMagic) continue;
      Fleet_AddOrder((long)ticket,
         OrderGetString(ORDER_SYMBOL),
         (int)OrderGetInteger(ORDER_TYPE)+1,
         OrderGetDouble(ORDER_VOLUME_INITIAL),
         OrderGetDouble(ORDER_VOLUME_CURRENT),
         OrderGetDouble(ORDER_PRICE_OPEN),
         OrderGetDouble(ORDER_PRICE_CURRENT),
         OrderGetDouble(ORDER_PRICE_STOPLIMIT),
         OrderGetDouble(ORDER_SL),
         OrderGetDouble(ORDER_TP),
         OrderGetString(ORDER_COMMENT),
         OrderGetInteger(ORDER_MAGIC),
         (long)OrderGetInteger(ORDER_TIME_SETUP_MSC),
         (long)OrderGetInteger(ORDER_TIME_EXPIRATION)*1000);
   }
   Fleet_EndResult(PAYLOAD_PENDING);
}

void HandleModifyPosition(const string id)
{
   long   ticket=Fleet_CmdInt("ticket");
   double sl    =Fleet_CmdDouble("stop_loss");
   double tp    =Fleet_CmdDouble("take_profit");
   if(!PositionSelectByTicket((ulong)ticket))
   {
      Fleet_SendError(id,MTF_ERR_NOT_FOUND,(int)GetLastError(),"position not found",0);
      return;
   }
   MqlTradeRequest req;
   MqlTradeResult  res;
   ZeroMemory(req);
   ZeroMemory(res);
   req.action  =TRADE_ACTION_SLTP;
   req.position=(ulong)ticket;
   req.symbol  =PositionGetString(POSITION_SYMBOL);
   req.sl      =(sl<0 ? PositionGetDouble(POSITION_SL) : sl); // negative = keep
   req.tp      =(tp<0 ? PositionGetDouble(POSITION_TP) : tp);
   if(!OrderSend(req,res) || !RetcodeOk(res.retcode))
   {
      Fleet_SendError(id,MapRetcode(res.retcode),(int)res.retcode,res.comment,
                        RetcodeRetryable(res.retcode)?1:0);
      return;
   }
   Fleet_SendAck(id);
}

void HandleCancelOrder(const string id)
{
   long ticket=Fleet_CmdInt("ticket");
   MqlTradeRequest req;
   MqlTradeResult  res;
   ZeroMemory(req);
   ZeroMemory(res);
   req.action=TRADE_ACTION_REMOVE;
   req.order =(ulong)ticket;
   if(!OrderSend(req,res) || !RetcodeOk(res.retcode))
   {
      Fleet_SendError(id,MapRetcode(res.retcode),(int)res.retcode,res.comment,
                        RetcodeRetryable(res.retcode)?1:0);
      return;
   }
   Fleet_SendAck(id);
}

void HandleModifyOrder(const string id)
{
   long   ticket   =Fleet_CmdInt("ticket");
   double price    =Fleet_CmdDouble("price");
   double stopLimit=Fleet_CmdDouble("stop_limit_price");
   double sl       =Fleet_CmdDouble("stop_loss");
   double tp       =Fleet_CmdDouble("take_profit");
   long   expMs    =Fleet_CmdInt("expiration_unix_ms");

   if(!OrderSelect((ulong)ticket))
   {
      Fleet_SendError(id,MTF_ERR_NOT_FOUND,(int)GetLastError(),"order not found",0);
      return;
   }
   MqlTradeRequest req;
   MqlTradeResult  res;
   ZeroMemory(req);
   ZeroMemory(res);
   req.action   =TRADE_ACTION_MODIFY;
   req.order    =(ulong)ticket;
   req.symbol   =OrderGetString(ORDER_SYMBOL);
   req.price    =(price>0     ? price     : OrderGetDouble(ORDER_PRICE_OPEN));
   req.stoplimit=(stopLimit>0 ? stopLimit : OrderGetDouble(ORDER_PRICE_STOPLIMIT));
   req.sl       =(sl<0 ? OrderGetDouble(ORDER_SL) : sl);
   req.tp       =(tp<0 ? OrderGetDouble(ORDER_TP) : tp);
   if(expMs>0)
   {
      req.type_time =ORDER_TIME_SPECIFIED;
      req.expiration=(datetime)(expMs/1000);
   }
   else if(expMs==0)
      req.type_time=ORDER_TIME_GTC;
   else // -1 keep as-is
   {
      req.type_time =(ENUM_ORDER_TYPE_TIME)OrderGetInteger(ORDER_TYPE_TIME);
      req.expiration=(datetime)OrderGetInteger(ORDER_TIME_EXPIRATION);
   }
   if(!OrderSend(req,res) || !RetcodeOk(res.retcode))
   {
      Fleet_SendError(id,MapRetcode(res.retcode),(int)res.retcode,res.comment,
                        RetcodeRetryable(res.retcode)?1:0);
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
      // them all; SymbolInfo* reads work unselected.
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
   datetime toT  =(toMs>0 ? (datetime)(toMs/1000) : TimeCurrent());
   if(!HistorySelect(fromT,toT))
   {
      Fleet_SendError(id,MTF_ERR_INTERNAL,(int)GetLastError(),"HistorySelect failed",1);
      return;
   }
   Fleet_BeginResult(id);
   int total=HistoryDealsTotal();
   for(int i=0;i<total;i++)
   {
      ulong ticket=HistoryDealGetTicket(i);
      if(ticket==0) continue;
      if(HistoryDealGetInteger(ticket,DEAL_TYPE)>DEAL_TYPE_SELL) continue;
      string s=HistoryDealGetString(ticket,DEAL_SYMBOL);
      if(fSymbol!="" && s!=fSymbol) continue;
      if(fMagic!=0 && HistoryDealGetInteger(ticket,DEAL_MAGIC)!=fMagic) continue;
      long entry=HistoryDealGetInteger(ticket,DEAL_ENTRY);
      int dealEntry=entry==DEAL_ENTRY_IN ? 1 : entry==DEAL_ENTRY_OUT ? 2
                    : entry==DEAL_ENTRY_INOUT ? 3 : entry==DEAL_ENTRY_OUT_BY ? 4 : 0;
      Fleet_AddDealV3((long)ticket,
         (long)HistoryDealGetInteger(ticket,DEAL_ORDER),
         (long)HistoryDealGetInteger(ticket,DEAL_POSITION_ID),
         s,
         (int)HistoryDealGetInteger(ticket,DEAL_TYPE)+1,
         HistoryDealGetDouble(ticket,DEAL_VOLUME),
         HistoryDealGetDouble(ticket,DEAL_PRICE),
         HistoryDealGetDouble(ticket,DEAL_PROFIT),
         HistoryDealGetDouble(ticket,DEAL_SWAP),
         HistoryDealGetDouble(ticket,DEAL_COMMISSION),
         HistoryDealGetString(ticket,DEAL_COMMENT),
         HistoryDealGetInteger(ticket,DEAL_MAGIC),
         (long)HistoryDealGetInteger(ticket,DEAL_TIME_MSC),dealEntry,
         HistoryDealGetDouble(ticket,DEAL_FEE));
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
      // Re-subscribe: reset the dedupe baseline so the next tick always
      // flows, even if the stored one is somehow ahead of the feed.
      g_lastTickMs[idx]=0;
      g_lastBid[idx]=0;
      g_lastAsk[idx]=0;
      return;
   }
   int n=ArraySize(g_symbols);
   ArrayResize(g_symbols,n+1);
   ArrayResize(g_lastTickMs,n+1);
   ArrayResize(g_lastBid,n+1);
   ArrayResize(g_lastAsk,n+1);
   g_symbols[n]=symbol;
   g_lastTickMs[n]=0;
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
      g_lastTickMs[i]=g_lastTickMs[i+1];
      g_lastBid[i]=g_lastBid[i+1];
      g_lastAsk[i]=g_lastAsk[i+1];
   }
   ArrayResize(g_symbols,n-1);
   ArrayResize(g_lastTickMs,n-1);
   ArrayResize(g_lastBid,n-1);
   ArrayResize(g_lastAsk,n-1);
}

int DepthIndex(const string symbol)
{
   for(int i=0;i<ArraySize(g_depthSymbols);i++)
      if(g_depthSymbols[i]==symbol) return i;
   return -1;
}

void AddDepthSubscription(const string symbol)
{
   if(DepthIndex(symbol)>=0) return;
   int n=ArraySize(g_depthSymbols);
   ArrayResize(g_depthSymbols,n+1);
   g_depthSymbols[n]=symbol;
}

void RemoveDepthSubscription(const string symbol)
{
   int idx=DepthIndex(symbol);
   if(idx<0) return;
   int n=ArraySize(g_depthSymbols);
   for(int i=idx;i<n-1;i++)
      g_depthSymbols[i]=g_depthSymbols[i+1];
   ArrayResize(g_depthSymbols,n-1);
}

ENUM_ORDER_TYPE_FILLING FillingMode(const string symbol)
{
   long modes=SymbolInfoInteger(symbol,SYMBOL_FILLING_MODE);
   if((modes & SYMBOL_FILLING_FOK)!=0) return ORDER_FILLING_FOK;
   if((modes & SYMBOL_FILLING_IOC)!=0) return ORDER_FILLING_IOC;
   return ORDER_FILLING_RETURN;
}

ENUM_TIMEFRAMES MapTimeframe(const int tf)
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
   return PERIOD_CURRENT; // signals "invalid" to the caller
}

bool RetcodeOk(const uint retcode)
{
   return retcode==TRADE_RETCODE_DONE ||
          retcode==TRADE_RETCODE_PLACED ||
          retcode==TRADE_RETCODE_DONE_PARTIAL;
}

bool RetcodeRetryable(const uint retcode)
{
   switch(retcode)
   {
      case TRADE_RETCODE_REQUOTE:
      case TRADE_RETCODE_PRICE_CHANGED:
      case TRADE_RETCODE_PRICE_OFF:
      case TRADE_RETCODE_TIMEOUT:
      case TRADE_RETCODE_TOO_MANY_REQUESTS:
      case TRADE_RETCODE_CONNECTION:
         return true;
   }
   return false;
}

int MapRetcode(const uint retcode)
{
   switch(retcode)
   {
      case TRADE_RETCODE_REQUOTE:           return MTF_ERR_REQUOTE;
      case TRADE_RETCODE_PRICE_CHANGED:     return MTF_ERR_PRICE_CHANGED;
      case TRADE_RETCODE_PRICE_OFF:         return MTF_ERR_INVALID_PRICE;
      case TRADE_RETCODE_INVALID_PRICE:     return MTF_ERR_INVALID_PRICE;
      case TRADE_RETCODE_INVALID_STOPS:     return MTF_ERR_INVALID_STOPS;
      case TRADE_RETCODE_INVALID_VOLUME:    return MTF_ERR_INVALID_VOLUME;
      case TRADE_RETCODE_NO_MONEY:          return MTF_ERR_NOT_ENOUGH_MONEY;
      case TRADE_RETCODE_MARKET_CLOSED:     return MTF_ERR_MARKET_CLOSED;
      case TRADE_RETCODE_TRADE_DISABLED:    return MTF_ERR_TRADE_DISABLED;
      case TRADE_RETCODE_CLIENT_DISABLES_AT:return MTF_ERR_TRADE_DISABLED;
      case TRADE_RETCODE_SERVER_DISABLES_AT:return MTF_ERR_TRADE_DISABLED;
      case TRADE_RETCODE_TIMEOUT:           return MTF_ERR_TIMEOUT;
      case TRADE_RETCODE_TOO_MANY_REQUESTS: return MTF_ERR_TOO_MANY_REQUESTS;
      case TRADE_RETCODE_CONNECTION:       return MTF_ERR_CONNECTION_LOST;
      case TRADE_RETCODE_INVALID:           return MTF_ERR_INVALID_REQUEST;
      case TRADE_RETCODE_POSITION_CLOSED:   return MTF_ERR_NOT_FOUND;
      case TRADE_RETCODE_REJECT:            return MTF_ERR_REJECTED;
   }
   return MTF_ERR_REJECTED;
}
//+------------------------------------------------------------------+
