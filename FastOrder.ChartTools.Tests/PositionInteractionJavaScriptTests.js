"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const interaction = require("../FastOrder.ChartViewer/Web/position-interaction.js");

function geometry() {
    return {
        leftX: 100,
        rightX: 300,
        targetY: 100,
        entryY: 180,
        stopY: 240,
        topY: 100,
        bottomY: 240
    };
}

test("nearestBarIndex chooses the closest stable time anchor", () => {
    assert.equal(interaction.nearestBarIndex([100, 160, 220], 189), 1);
    assert.equal(interaction.nearestBarIndex([100, 160, 220], 190), 1);
    assert.equal(interaction.nearestBarIndex([100, 160, 220], 191), 2);
});

test("selected price lines expose vertical drag handles", () => {
    const positions = [{ id: "selected" }];
    const geometries = new Map([["selected", geometry()]]);
    assert.deepEqual(
        interaction.hitTest(positions, geometries, "selected", { x: 200, y: 180 }),
        { id: "selected", kind: "price-handle", handle: "Entry" });
});

test("selected horizontal edges expose width handles", () => {
    const positions = [{ id: "selected" }];
    const geometries = new Map([["selected", geometry()]]);
    assert.deepEqual(
        interaction.hitTest(positions, geometries, "selected", { x: 300, y: 170 }),
        { id: "selected", kind: "range-handle", handle: "EndEdge" });
});

test("an unselected line selects without beginning a drag", () => {
    const positions = [{ id: "other" }, { id: "selected" }];
    const geometries = new Map([
        ["other", geometry()],
        ["selected", { ...geometry(), leftX: 400, rightX: 500 }]
    ]);
    assert.deepEqual(
        interaction.hitTest(positions, geometries, "selected", { x: 200, y: 100 }),
        { id: "other", kind: "select" });
});

test("empty chart space is not claimed by a drawing", () => {
    const positions = [{ id: "selected" }];
    const geometries = new Map([["selected", geometry()]]);
    assert.equal(interaction.hitTest(positions, geometries, "selected", { x: 20, y: 20 }), null);
});

test("panel values reject unknown, non-positive, and fractional precision inputs", () => {
    assert.equal(interaction.isValidPropertyValue("entryPrice", 125), true);
    assert.equal(interaction.isValidPropertyValue("entryPrice", 0), false);
    assert.equal(interaction.isValidPropertyValue("quantityPrecision", 2.5), false);
    assert.equal(interaction.isValidPropertyValue("riskMode", "PercentOfAccount"), true);
    assert.equal(interaction.isValidPropertyValue("unknown", 1), false);
});

test("move deltas stay inside available bar anchors", () => {
    assert.equal(interaction.clampMoveDelta(10, 0, 5, 15, 20), -5);
    assert.equal(interaction.clampMoveDelta(10, 30, 5, 15, 20), 4);
});

test("horizontal resize preserves at least one bar", () => {
    assert.equal(interaction.clampResizeIndex("StartEdge", 15, 5, 10, 20), 9);
    assert.equal(interaction.clampResizeIndex("EndEdge", 0, 5, 10, 20), 6);
});

test("label layout stays visible and non-overlapping", () => {
    const tops = interaction.layoutLabelTops([100, 105, 110], 300, 22, 3);
    assert.equal(tops.length, 3);
    assert.ok(tops.every(top => top >= 0 && top <= 278));
    const sorted = tops.slice().sort((left, right) => left - right);
    assert.ok(sorted[1] - sorted[0] >= 25);
    assert.ok(sorted[2] - sorted[1] >= 25);
});
