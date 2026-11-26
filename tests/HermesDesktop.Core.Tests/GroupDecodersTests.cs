using System.Text.Json;
using HermesDesktop.Core.Models;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class GroupDecodersTests
{
    [Fact]
    public void DecodeRooms_ParsesRoomMembersAndLatestSeq()
    {
        using var doc = JsonDocument.Parse("""
        {
          "rooms": [
            {
              "room_id": "room-1",
              "name": "Architecture Council",
              "state": "active",
              "latest_seq": 14,
              "last_message_preview": "Agreed on interface.",
              "members": [
                { "member_id": "m1", "profile": "architect", "handle": "architect", "display_name": "Architect" },
                { "member_id": "m2", "profile": "reviewer", "handle": "reviewer", "display_name": "Reviewer" }
              ]
            }
          ]
        }
        """);

        var rooms = GroupDecoders.DecodeRooms(doc.RootElement);
        Assert.Single(rooms);
        var room = rooms[0];
        Assert.Equal("room-1", room.RoomId);
        Assert.Equal("Architecture Council", room.Name);
        Assert.Equal(14, room.LatestSeq);
        Assert.Equal(2, room.Members.Count);
        Assert.Equal("@architect", room.Members[0].HandleDisplay);
    }

    [Fact]
    public void DecodeLogPage_ExtractsEventsAndCursor()
    {
        using var doc = JsonDocument.Parse("""
        {
          "events": [
            {
              "room_id": "room-1",
              "seq": 5,
              "event_id": "ev-5",
              "kind": "message.member",
              "actor": { "kind": "member", "id": "m1", "profile": "architect", "display_name": "Architect" },
              "payload": { "text": "Use WebSockets for live state.", "thread_id": "ev-1" },
              "created_at": 1764000000
            }
          ],
          "cursor": 5,
          "latest_seq": 5,
          "has_more": false
        }
        """);

        var page = GroupDecoders.DecodeLogPage(doc.RootElement);
        Assert.Single(page.Events);
        var ev = page.Events[0];
        Assert.True(ev.IsMessage);
        Assert.True(ev.IsMemberMessage);
        Assert.Equal("Architect", ev.Actor.DisplayName);
        Assert.Equal("Use WebSockets for live state.", ev.Text);
        Assert.Equal("ev-1", ev.ThreadId);
    }

    [Fact]
    public void DecodeState_ExtractsContextDriverAndPendingApprovals()
    {
        using var doc = JsonDocument.Parse("""
        {
          "room": {
            "room_id": "room-1",
            "name": "Review Room",
            "state": "active",
            "latest_seq": 2,
            "members": []
          },
          "context": {
            "purpose": "Review PR",
            "notes": "Focus on thread safety",
            "routing": { "mode": "lead", "lead_member_id": "m1", "max_rounds": 4 }
          },
          "driver_status": {
            "running": true,
            "counts": { "working": 1, "blocked": 1 },
            "max_continues": { "can_continue": true, "discussion_event_id": "ev-9" },
            "pending_actions": [
              { "member_id": "m1", "task_id": "t-1", "execution_generation": 2, "request_id": "req-9", "command": "rm -rf /tmp/x" }
            ]
          }
        }
        """);

        var snap = GroupDecoders.DecodeState(doc.RootElement);
        Assert.NotNull(snap);
        Assert.Equal("Review PR", snap!.Context.Purpose);
        Assert.Equal("lead", snap.Context.Routing.Mode);
        Assert.Equal("m1", snap.Context.Routing.LeadMemberId);
        Assert.True(snap.DriverStatus.Running);
        Assert.Equal(1, snap.DriverStatus.WorkingCount);
        Assert.True(snap.DriverStatus.CanContinue);
        Assert.Equal("ev-9", snap.DriverStatus.ContinueDiscussionEventId);
        Assert.Single(snap.DriverStatus.PendingApprovals);
        Assert.Equal("req-9", snap.DriverStatus.PendingApprovals[0].RequestId);
    }
}
