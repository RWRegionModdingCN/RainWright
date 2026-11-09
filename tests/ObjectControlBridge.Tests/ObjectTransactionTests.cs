using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Compatibility;

internal static class ObjectTransactionTests
{
    private sealed class Snapshot { internal readonly int Value; internal Snapshot(int value)=>Value=value; }
    internal static int Run()
    {
        int checks=0, value=10, restores=0;
        void Check(bool test,string message){if(!test)throw new Exception(message);checks++;}
        Snapshot Capture()=>new(value);
        string Fingerprint(Snapshot state)=>state.Value.ToString();
        bool Restore(Snapshot state){restores++;value=state.Value;return true;}
        var reports=new List<string>();
        void Report(string message,Exception error)=>reports.Add(message+(error==null?"":" "+error));
        int result=ObjectCompatibilityTransactions.Read(Capture,Fingerprint,Restore,()=>{value=20;return 42;},Report);
        Check(result==42&&value==10&&restores==1&&reports.Count==1,"Read-only UI effects restore author data while retaining the read result.");
        ObjectCompatibilityTransactions.Read(Capture,Fingerprint,Restore,()=>0,Report);
        Check(restores==1,"Normal read-only publication performs no unnecessary author restore.");
        var original=new InvalidOperationException("original read callback failed");
        try {ObjectCompatibilityTransactions.Read<Snapshot,int>(Capture,Fingerprint,Restore,()=>{value=99;throw original;},Report);throw new Exception("Expected read failure");}
        catch(InvalidOperationException error){Check(ReferenceEquals(error,original)&&value==10,"Original read exceptions are preserved after author recovery.");}
        bool success=ObjectCompatibilityTransactions.Edit(Capture,Fingerprint,Restore,()=>{value=30;return false;},Report,out var before,out var after);
        Check(!success&&value==10&&before.Value==10&&after.Value==10,"Rejected callbacks roll back partial author writes and do not claim success.");
        success=ObjectCompatibilityTransactions.Edit(Capture,Fingerprint,Restore,()=>{value=40;throw new InvalidOperationException("original edit failed");},Report,out before,out after);
        Check(!success&&value==10&&reports.Exists(r=>r.Contains("original edit failed")),"Throwing edits retain their original error and recover author data.");
        success=ObjectCompatibilityTransactions.Edit(Capture,Fingerprint,Restore,()=>{value=50;return true;},Report,out before,out after);
        Check(success&&value==50&&before.Value==10&&after.Value==50,"Successful original edits retain before/after history snapshots.");
        int calls=0;
        success=ObjectCompatibilityTransactions.Edit<Snapshot>(()=>null,Fingerprint,Restore,()=>{calls++;return true;},Report,out before,out after);
        Check(!success&&calls==0,"Edits do not run without a recoverable author snapshot.");
        value=10;
        try{ObjectCompatibilityTransactions.Read<Snapshot,int>(Capture,Fingerprint,_=>false,()=>{value=60;throw original;},Report);throw new Exception("Expected aggregate");}
        catch(AggregateException error){Check(error.InnerExceptions.Contains(original)&&error.InnerExceptions.Count==2,"Combined read/rollback failure retains both original and recovery exceptions.");}
        value=10;
        success=ObjectCompatibilityTransactions.Edit(Capture,Fingerprint,_=>true,()=>{value=70;return false;},Report,out before,out after);
        Check(!success&&after.Value==70&&reports.Exists(r=>r.Contains("did not reproduce")),"A lying restore callback cannot report successful recovery; residual data remains recoverable through history.");
        value=10; int reads=0;
        Snapshot FaultyCapture(){if(++reads==2)throw new InvalidOperationException("verification failed");return Capture();}
        try{ObjectCompatibilityTransactions.Read<Snapshot,int>(FaultyCapture,Fingerprint,Restore,()=>{value=80;return 1;},Report);throw new Exception("Expected verification failure");}
        catch(InvalidOperationException error){Check(error.Message=="verification failed"&&value==10,"Failed post-read verification still attempts author recovery and retains the original verification error.");}
        return checks;
    }
}
