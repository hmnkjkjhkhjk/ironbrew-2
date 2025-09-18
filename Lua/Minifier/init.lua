-- LuaSrcDiet API (CLI copy) — with Luau→Lua5.1 normalize step

-- make sure this folder is on package.path
local here = (...):gsub("[^%.]+$", "")  -- e.g. "Lua.Minifier."
package.path = package.path
  .. ";./?.lua;./?/init.lua"
  .. ";../Lua/Minifier/?.lua;../Lua/Minifier/?/init.lua"

local llex      = require 'llex'
local lparser   = require 'lparser'
local optlex    = require 'optlex'
local optparser = require 'optparser'
local utils     = require 'utils'

-- try both module names so it works no matter where the CLI is run from
local luaucompat
do
  local ok, mod = pcall(require, 'luaucompat')
  if ok then luaucompat = mod
  else
    ok, mod = pcall(require, 'Lua.Minifier.luaucompat')
    if ok then luaucompat = mod end
  end
end

local concat = table.concat
local merge  = utils.merge

local function noop() end

local function opts_to_legacy (opts)
  local res = {}
  for k, v in pairs(opts) do
    res['opt-'..k] = v
  end
  return res
end

local M = {}

M._NAME     = 'luasrcdiet'
M._VERSION  = '1.0.0'
M._HOMEPAGE = 'https://github.com/jirutka/luasrcdiet'

M.NONE_OPTS = {
  binequiv=false, comments=false, emptylines=false, entropy=false,
  eols=false, experimental=false, locals=false, numbers=false,
  srcequiv=false, strings=false, whitespace=false,
}

M.BASIC_OPTS   = merge(M.NONE_OPTS, { comments=true, emptylines=true, srcequiv=true, whitespace=true })
M.DEFAULT_OPTS = merge(M.BASIC_OPTS, { locals=true, numbers=true })
M.MAXIMUM_OPTS = merge(M.DEFAULT_OPTS, { entropy=true, eols=true, strings=true })

function M.optimize (opts, source)
  assert(type(source) == 'string', 'bad argument #2: expected string')

  opts = opts and merge(M.NONE_OPTS, opts) or M.DEFAULT_OPTS
  local legacy_opts = opts_to_legacy(opts)

  -- --- Luau → Lua 5.1 normalize (fixes `continue`, `+=`, `..=`, etc.)
  if luaucompat and luaucompat.normalize then
    source = luaucompat.normalize(source)
  else
    -- last-ditch: warn once so you know if normalize didn’t run
    print("[luasrcdiet] WARN: luaucompat not found; using raw input (Luau syntax may fail under luac).")
  end

  -- DEBUG: write normalized source next to the CLI binary
  local f, err = io.open("normalized.lua", "w")
  if f then f:write(source); f:close(); print("[luasrcdiet] normalized.lua written") else print("[luasrcdiet] write fail:", err) end

  -- tokenize & parse
  local toklist, seminfolist, toklnlist = llex.lex(source)
  local xinfo = lparser.parse(toklist, seminfolist, toklnlist)

  -- optimize
  optparser.print = noop
  optparser.optimize(legacy_opts, toklist, seminfolist, xinfo)

  optlex.print = noop
  local _, seminfolist2 = optlex.optimize(legacy_opts, toklist, seminfolist, toklnlist)

  return concat(seminfolist2)
end

return M
