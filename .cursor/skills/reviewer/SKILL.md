---
name: reviewer
description: 定义项目级 PR 指派人与审查人优先顺序。
---

# 项目级 reviewer 指派人

## 目标
- 为项目提供**有序**指派人列表，用于 PR/MR 创建时的 assignee 与 reviewer
- 规则：第一个为 assignee，其余为 reviewer

## 使用规则
- 当需要更新指派人列表时：
  - 获取当前项目具备权限的成员（Maintainer/Owner，排除 root）
  - 列出成员给用户**多选**，并尊重用户选择顺序
  - 将结果写入本文件

## 存储格式
- 每行一个用户名，按优先顺序排列

## 当前指派人列表
guoxdxdxd
